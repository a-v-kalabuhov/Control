<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Шаблоны оборудования</h2>
        <p class="text-gray-600 mt-1">Конфигурация датчиков и параметров для типов ТПА</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Новый шаблон
      </el-button>
    </div>

    <!-- Таблица шаблонов -->
    <el-table :data="templates" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="name" label="Наименование" />
      <el-table-column prop="manufacturer" label="Производитель" width="150" />
      <el-table-column prop="model" label="Модель" width="150" />
      <el-table-column prop="version" label="Версия" width="100" />
      <el-table-column prop="author" label="Автор" width="150" />
      <el-table-column prop="sensorCount" label="Датчиков" width="100" align="center" />
      <el-table-column prop="createdAt" label="Создан" width="180">
        <template #default="{ row }">
          {{ formatDate(row.createdAt) }}
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="200" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editTemplate(row)">Редактировать</el-button>
          <el-button size="small" type="danger" @click="deleteTemplate(row)">Удалить</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- Модальное окно создания/редактирования -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingTemplate ? 'Редактирование шаблона' : 'Новый шаблон'"
      width="800px"
    >
      <el-form :model="form" label-width="150px">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Наименование" required>
              <el-input v-model="form.name" placeholder="Станок №5 (Основной цех)" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Производитель">
              <el-input v-model="form.manufacturer" placeholder="Haitian, Siger, Sallsen" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Модель">
              <el-input v-model="form.model" placeholder="MA1200, 160v5, 200s1" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Версия">
              <el-input v-model="form.version" placeholder="1.0" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item label="Автор">
          <el-input v-model="form.author" placeholder="Wintime" />
        </el-form-item>

        <el-form-item label="JSON-конфигурация" required>
          <el-input
            v-model="form.jsonConfigString"
            type="textarea"
            :rows="15"
            placeholder='{"sensors": [{"name": "Температура зоны 1", "field": "temp_zone_1", "type": "float", "threshold": 0.5}]}'
          />
          <div class="text-sm text-gray-500 mt-2">
            💡 Пример конфигурации датчиков с порогами COV-фильтрации
          </div>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveTemplate" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { templatesApi } from '@/api/templates'
import dayjs from 'dayjs'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingTemplate = ref(null)
const templates = ref([])

const form = reactive({
  name: '',
  manufacturer: '',
  model: '',
  version: '1.0',
  author: '',
  jsonConfigString: ''
})

onMounted(async () => {
  await loadTemplates()
})

const loadTemplates = async () => {
  loading.value = true
  try {
    const response = await templatesApi.getList()
    templates.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки шаблонов')
  } finally {
    loading.value = false
  }
}

const showCreateModal = () => {
  editingTemplate.value = null
  Object.assign(form, {
    name: '',
    manufacturer: '',
    model: '',
    version: '1.0',
    author: '',
    jsonConfigString: ''
  })
  dialogVisible.value = true
}

const editTemplate = (template) => {
  editingTemplate.value = template
  Object.assign(form, {
    name: template.name,
    manufacturer: template.manufacturer,
    model: template.model,
    version: template.version,
    author: template.author,
    jsonConfigString: '' // TODO: Загрузить полный JSON из бэкенда
  })
  dialogVisible.value = true
}

const saveTemplate = async () => {
  if (!form.name) {
    ElMessage.warning('Введите наименование шаблона')
    return
  }

  let jsonConfig
  try {
    jsonConfig = form.jsonConfigString ? JSON.parse(form.jsonConfigString) : {}
  } catch (error) {
    ElMessage.error('Неверный формат JSON-конфигурации')
    return
  }

  saving.value = true
  try {
    const data = {
      name: form.name,
      manufacturer: form.manufacturer,
      model: form.model,
      version: form.version,
      author: form.author,
      jsonConfig
    }

    if (editingTemplate.value) {
      await templatesApi.update(editingTemplate.value.id, data)
      ElMessage.success('Шаблон обновлён')
    } else {
      await templatesApi.create(data)
      ElMessage.success('Шаблон создан')
    }

    dialogVisible.value = false
    await loadTemplates()
  } catch (error) {
    ElMessage.error('Ошибка сохранения шаблона')
  } finally {
    saving.value = false
  }
}

const deleteTemplate = async (template) => {
  try {
    await ElMessageBox.confirm(`Удалить шаблон "${template.name}"?`, 'Подтверждение', {
      type: 'warning'
    })
    
    await templatesApi.delete(template.id)
    ElMessage.success('Шаблон удалён')
    await loadTemplates()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка удаления шаблона')
    }
  }
}

const formatDate = (date) => {
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}
</script>