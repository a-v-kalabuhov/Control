<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник пресс-форм</h2>
        <p class="text-gray-600 mt-1">Управление оснасткой и учёт ресурса</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить пресс-форму
      </el-button>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Наименование или артикул" clearable />
        </el-form-item>
        <el-form-item label="Статус">
          <el-select v-model="filters.isActive" placeholder="Все" clearable>
            <el-option label="Активные" :value="true" />
            <el-option label="Не активные" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadMolds">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Таблица пресс-форм -->
    <el-table :data="molds" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="formId" label="Артикул" width="120" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column prop="cavities" label="Гнёзд" width="80" align="center" />
      <el-table-column label="Вес (г)" width="120">
        <template #default="{ row }">
          {{ row.partWeightGrams }} + {{ row.runnerWeightGrams }}
        </template>
      </el-table-column>
      <el-table-column prop="storageLocationIndex" label="Место хранения" width="120" />
      <el-table-column label="Ресурс" width="150">
        <template #default="{ row }">
          <el-progress 
            :percentage="Math.round((row.remainingResource / row.maxResourceCycles) * 100)" 
            :status="row.remainingResource < row.to2Cycles ? 'exception' : row.remainingResource < row.to1Cycles ? 'warning' : ''"
          />
          <div class="text-xs text-gray-500">{{ row.remainingResource }} / {{ row.maxResourceCycles }}</div>
        </template>
      </el-table-column>
      <el-table-column label="Статус" width="100" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активна' : 'Не активна' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="200" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editMold(row)">Редактировать</el-button>
          <el-button size="small" type="danger" @click="deleteMold(row)">Удалить</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- Модальное окно -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingMold ? 'Редактирование пресс-формы' : 'Новая пресс-форма'"
      width="700px"
    >
      <el-form :model="form" label-width="180px" :rules="rules" ref="formRef">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Наименование" prop="name" required>
              <el-input v-model="form.name" placeholder="КлипДак (48)" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Количество гнёзд" prop="cavities" required>
              <el-input-number v-model="form.cavities" :min="1" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Вес детали (г)">
              <el-input-number v-model="form.partWeightGrams" :precision="2" :step="0.1" class="w-full" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Вес литника (г)">
              <el-input-number v-model="form.runnerWeightGrams" :precision="2" :step="0.1" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Ресурс (смыкания)" required>
              <el-input-number v-model="form.maxResourceCycles" :min="1" class="w-full" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Место хранения">
              <el-input v-model="form.storageLocationIndex" placeholder="А-12" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="ТО-1 (смыкания)">
              <el-input-number v-model="form.to1Cycles" :min="0" class="w-full" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="ТО-2 (смыкания)">
              <el-input-number v-model="form.to2Cycles" :min="0" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item label="Статус">
          <el-switch v-model="form.isActive" active-text="Активна" inactive-text="Не активна" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveMold" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { moldsApi } from '@/api/molds'
import dayjs from 'dayjs'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingMold = ref(null)
const formRef = ref(null)

const molds = ref([])

const filters = reactive({
  search: '',
  isActive: null
})

const form = reactive({
  name: '',
  cavities: 1,
  partWeightGrams: 0,
  runnerWeightGrams: 0,
  maxResourceCycles: 0,
  to1Cycles: 0,
  to2Cycles: 0,
  storageLocationIndex: '',
  isActive: true
})

const rules = {
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }],
  cavities: [{ required: true, message: 'Укажите гнёздность', trigger: 'blur' }],
  maxResourceCycles: [{ required: true, message: 'Укажите ресурс', trigger: 'blur' }]
}

onMounted(async () => {
  await loadMolds()
})

const loadMolds = async () => {
  loading.value = true
  try {
    const response = await moldsApi.getList({ 
      isActive: filters.isActive, 
      search: filters.search 
    })
    molds.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки пресс-форм')
  } finally {
    loading.value = false
  }
}

const showCreateModal = () => {
  editingMold.value = null
  Object.assign(form, {
    name: '',
    cavities: 1,
    partWeightGrams: 0,
    runnerWeightGrams: 0,
    maxResourceCycles: 0,
    to1Cycles: 0,
    to2Cycles: 0,
    storageLocationIndex: '',
    isActive: true
  })
  dialogVisible.value = true
}

const editMold = (mold) => {
  editingMold.value = mold
  Object.assign(form, {
    name: mold.name,
    cavities: mold.cavities,
    partWeightGrams: mold.partWeightGrams,
    runnerWeightGrams: mold.runnerWeightGrams,
    maxResourceCycles: mold.maxResourceCycles,
    to1Cycles: mold.to1Cycles,
    to2Cycles: mold.to2Cycles,
    storageLocationIndex: mold.storageLocationIndex,
    isActive: mold.isActive
  })
  dialogVisible.value = true
}

const saveMold = async () => {
  if (!formRef.value) return
  
  await formRef.value.validate(async (valid) => {
    if (!valid) return

    saving.value = true
    try {
      if (editingMold.value) {
        await moldsApi.update(editingMold.value.id, form)
        ElMessage.success('Пресс-форма обновлена')
      } else {
        await moldsApi.create(form)
        ElMessage.success('Пресс-форма создана')
      }

      dialogVisible.value = false
      await loadMolds()
    } catch (error) {
      ElMessage.error('Ошибка сохранения пресс-формы')
    } finally {
      saving.value = false
    }
  })
}

const deleteMold = async (mold) => {
  try {
    await ElMessageBox.confirm(`Деактивировать пресс-форму "${mold.name}"?`, 'Подтверждение', {
      type: 'warning'
    })
    
    await moldsApi.update(mold.id, { isActive: false })
    ElMessage.success('Пресс-форма деактивирована')
    await loadMolds()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка удаления пресс-формы')
    }
  }
}
</script>