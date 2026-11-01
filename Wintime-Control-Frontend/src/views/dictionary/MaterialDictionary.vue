<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">{{ texts.title }}</h2>
        <p class="text-gray-600 mt-1">{{ texts.subtitle }}</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        {{ texts.add }}
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Артикул, наименование, производитель, цвет" clearable />
        </el-form-item>
        <el-form-item label="Статус">
          <el-select v-model="filters.isActive" placeholder="Все" clearable style="width: 160px;">
            <el-option label="Активные" :value="true" />
            <el-option label="Не активные" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadItems">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-table :data="items" stripe style="width: 100%" v-loading="loading" row-key="id">
      <el-table-column prop="article" label="Артикул" width="160" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column prop="manufacturer" label="Производитель" width="200" />
      <el-table-column label="Цвет" width="220">
        <template #default="{ row }">{{ rowColorLabel(row) }}</template>
      </el-table-column>
      <el-table-column label="Статус" width="120" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">{{ row.isActive ? 'Активен' : 'В архиве' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="300" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editItem(row)">Редактировать</el-button>
          <el-button size="small" :type="row.isActive ? 'danger' : 'success'" @click="toggleArchive(row)">
            {{ row.isActive ? 'В архив' : 'Вернуть' }}
          </el-button>
          <el-button v-if="!row.isUsed" class="material-delete" size="small" type="danger" plain @click="deleteItem(row)">
            Удалить
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="dialogVisible"
      :title="editing ? texts.editTitle : texts.createTitle"
      width="560px"
    >
      <el-form :model="form" label-width="140px">
        <el-form-item label="Артикул" required>
          <el-input v-model="form.article" placeholder="PP-01" @input="onArticleInput" />
        </el-form-item>
        <el-form-item label="Наименование" required>
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="Производитель">
          <SuggestInput v-model="form.manufacturer" :fetch="fetchManufacturers" />
        </el-form-item>
        <el-form-item label="Цвет" :required="isColorant">
          <SuggestInput
            v-model="form.colorName"
            :fetch="fetchColors"
            :item-label="suggestionColorLabel"
            :item-value="c => c.name"
            placeholder="Наименование"
            @select="onColorSelect"
          />
        </el-form-item>
        <el-form-item label="Код цвета">
          <el-input v-model="form.colorCode" placeholder="RAL 9005" />
        </el-form-item>
        <el-form-item label="Комментарий">
          <el-input v-model="form.comment" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="save" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { materialsApi } from '@/api/materials'
import { apiErrorMessage } from '@/utils/apiError'
import SuggestInput from '@/components/common/SuggestInput.vue'

// Справочник материалов (PZP-22): один компонент для пластиков и красителей, тип — из маршрута.
const props = defineProps({
  materialType: { type: String, required: true }   // 'Plastic' | 'Colorant'
})

const TEXTS = {
  Plastic: {
    title: 'Справочник пластиков', subtitle: 'Пластики — основа рецептов изделий', add: 'Добавить пластик',
    createTitle: 'Новый пластик', editTitle: 'Редактирование пластика', one: 'Пластик'
  },
  Colorant: {
    title: 'Справочник красителей', subtitle: 'Красители для рецептов изделий', add: 'Добавить краситель',
    createTitle: 'Новый краситель', editTitle: 'Редактирование красителя', one: 'Краситель'
  }
}

// Текст ошибки сервера; нестроковое тело (ProblemDetails и т.п.) заменяется запасным текстом.
function errText(error, fallback) {
  const msg = apiErrorMessage(error, fallback)
  return typeof msg === 'string' && msg ? msg : fallback
}

const texts = computed(() => TEXTS[props.materialType])
const isColorant = computed(() => props.materialType === 'Colorant')

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref(null)
const items = ref([])

const filters = reactive({ search: '', isActive: null })
const emptyForm = () => ({ article: '', name: '', manufacturer: '', colorName: '', colorCode: '', comment: '' })
const form = reactive(emptyForm())

onMounted(loadItems)
// Переход «Пластики» ↔ «Красители» переиспользует экземпляр компонента — перезагрузить список.
watch(() => props.materialType, loadItems)

// Строка таблицы: только цвет материала (наименование материала сюда не подставляется).
function rowColorLabel(row) {
  return [row.colorName, row.colorCode].filter(Boolean).join(' · ')
}

// Подсказка цвета: { name, code }.
function suggestionColorLabel(c) {
  return [c.name, c.code].filter(Boolean).join(' · ')
}

async function loadItems() {
  loading.value = true
  try {
    const { data } = await materialsApi.getList({
      type: props.materialType,
      isActive: filters.isActive,
      search: filters.search
    })
    items.value = data
  } catch {
    ElMessage.error('Ошибка загрузки материалов')
  } finally {
    loading.value = false
  }
}

const fetchManufacturers = async (search) => (await materialsApi.suggestManufacturers(search)).data
const fetchColors = async (search) => (await materialsApi.suggestColors(search)).data

function onArticleInput() {
  form.article = form.article.toUpperCase()
}

function onColorSelect(c) {
  form.colorName = c.name
  form.colorCode = c.code ?? ''
}

function showCreateModal() {
  editing.value = null
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

function editItem(row) {
  editing.value = row
  Object.assign(form, {
    article: row.article, name: row.name, manufacturer: row.manufacturer ?? '',
    colorName: row.colorName ?? '', colorCode: row.colorCode ?? '', comment: row.comment ?? ''
  })
  dialogVisible.value = true
}

async function save() {
  const payload = {
    article: form.article.trim().toUpperCase(),
    name: form.name.trim(),
    // Пустая строка — «очистить поле» (PUT частичный: null значил бы «не менять»).
    manufacturer: form.manufacturer.trim(),
    colorName: form.colorName.trim(),
    colorCode: form.colorCode.trim(),
    comment: form.comment.trim()
  }
  if (!payload.article || !payload.name) {
    ElMessage.error('Введите артикул и наименование')
    return
  }
  if (isColorant.value && !payload.colorName) {
    ElMessage.error('Для красителя укажите цвет')
    return
  }
  saving.value = true
  try {
    if (editing.value) {
      await materialsApi.update(editing.value.id, payload)
      ElMessage.success(`${texts.value.one} обновлён`)
    } else {
      await materialsApi.create({ type: props.materialType, ...payload })
      ElMessage.success(`${texts.value.one} добавлен`)
    }
    dialogVisible.value = false
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка сохранения материала'))
    return
  } finally {
    saving.value = false
  }
  await loadItems()
}

async function toggleArchive(row) {
  try {
    await materialsApi.update(row.id, { isActive: !row.isActive })
    ElMessage.success(row.isActive ? `${texts.value.one} в архиве` : `${texts.value.one} возвращён`)
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка изменения статуса'))
    return
  }
  await loadItems()
}

async function deleteItem(row) {
  try {
    await ElMessageBox.confirm(`Удалить ${texts.value.one.toLowerCase()} ${row.article}?`, 'Подтверждение', {
      confirmButtonText: 'Удалить', cancelButtonText: 'Отмена', type: 'warning'
    })
  } catch {
    return
  }
  try {
    await materialsApi.remove(row.id)
    ElMessage.success(`${texts.value.one} удалён`)
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка удаления материала'))
    return
  }
  await loadItems()
}

defineExpose({ form, showCreateModal, editItem, save, toggleArchive, deleteItem, onArticleInput, onColorSelect })
</script>
