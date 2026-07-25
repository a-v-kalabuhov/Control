<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник изделий</h2>
        <p class="text-gray-600 mt-1">Номенклатура выпускаемых изделий</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить изделие
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Артикул или наименование" clearable />
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

    <el-table :data="items" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="article" label="Артикул" width="200" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column label="Статус" width="120" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активно' : 'В архиве' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="220" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editItem(row)">Редактировать</el-button>
          <el-button size="small" :type="row.isActive ? 'danger' : 'success'" @click="toggleArchive(row)">
            {{ row.isActive ? 'В архив' : 'Вернуть' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="dialogVisible"
      :title="editing ? 'Редактирование изделия' : 'Новое изделие'"
      width="500px"
    >
      <el-form :model="form" label-width="140px" :rules="rules" ref="formRef">
        <el-form-item label="Артикул" prop="article" required>
          <el-input v-model="form.article" placeholder="ART-001" @input="form.article = form.article.toUpperCase()" />
        </el-form-item>
        <el-form-item label="Наименование" prop="name" required>
          <el-input v-model="form.name" placeholder="Крышка 48мм" />
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
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { productTypesApi } from '@/api/productTypes'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref(null)
const formRef = ref(null)
const items = ref([])

const filters = reactive({ search: '', isActive: null })
const form = reactive({ article: '', name: '' })

const rules = {
  article: [{ required: true, message: 'Введите артикул', trigger: 'blur' }],
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }]
}

onMounted(loadItems)

async function loadItems() {
  loading.value = true
  try {
    const { data } = await productTypesApi.getList({
      isActive: filters.isActive,
      search: filters.search
    })
    items.value = data
  } catch {
    ElMessage.error('Ошибка загрузки изделий')
  } finally {
    loading.value = false
  }
}

function showCreateModal() {
  editing.value = null
  Object.assign(form, { article: '', name: '' })
  dialogVisible.value = true
}

function editItem(row) {
  editing.value = row
  Object.assign(form, { article: row.article, name: row.name })
  dialogVisible.value = true
}

async function save() {
  if (!formRef.value) return
  await formRef.value.validate(async (valid) => {
    if (!valid) return
    saving.value = true
    try {
      if (editing.value) {
        await productTypesApi.update(editing.value.id, form)
        ElMessage.success('Изделие обновлено')
      } else {
        await productTypesApi.create(form)
        ElMessage.success('Изделие создано')
      }
      dialogVisible.value = false
      await loadItems()
    } catch (error) {
      ElMessage.error(error.response?.data ?? 'Ошибка сохранения изделия')
    } finally {
      saving.value = false
    }
  })
}

async function toggleArchive(row) {
  try {
    await productTypesApi.update(row.id, { isActive: !row.isActive })
    ElMessage.success(row.isActive ? 'Изделие в архиве' : 'Изделие возвращено')
    await loadItems()
  } catch {
    ElMessage.error('Ошибка изменения статуса')
  }
}
</script>
