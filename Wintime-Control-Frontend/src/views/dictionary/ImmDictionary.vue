<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник ТПА</h2>
        <p class="text-gray-600 mt-1">Управление термопластавтоматами</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить ТПА
      </el-button>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
        <el-form :inline="true" :model="filters">
          <el-form-item label="Статус">
            <el-select v-model="filters.isActive" placeholder="Все" clearable style="width: 160px;">
              <el-option label="Активные" :value="true" />
              <el-option label="Не активные" :value="false" />
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" @click="loadImms">Применить</el-button>
          </el-form-item>
        </el-form>
    </el-card>

    <!-- Таблица ТПА -->
    <el-table :data="imms" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="name" label="Наименование" />
      <el-table-column prop="inventoryNumber" label="Инвентарный номер" width="150" />
      <el-table-column prop="manufacturer" label="Производитель" width="150" />
      <el-table-column prop="model" label="Модель" width="150" />
      <el-table-column label="Статус" width="100" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активен' : 'Не активен' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Введён в эксплуатацию" width="180">
        <template #default="{ row }">
          {{ row.commissioningDate ? formatDate(row.commissioningDate, false) : '—' }}
        </template>
      </el-table-column>
      <el-table-column prop="createdAt" label="Создан" width="180">
        <template #default="{ row }">
          {{ formatDate(row.createdAt) }}
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="200" fixed="right">
        <template #default="{ row }">
          <div class="flex flex-col items-start gap-1">
            <el-button size="small" style="width: 130px; margin: 0" @click="editImm(row)">Редактировать</el-button>
            <el-button size="small" style="width: 130px; margin: 0" @click="showQr(row)">QR</el-button>
            <el-button size="small" style="width: 130px; margin: 0" type="danger" @click="deleteImm(row)">Удалить</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <!-- Модальное окно -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingImm ? 'Редактирование ТПА' : 'Новый ТПА'"
      width="600px"
    >
      <el-form :model="form" label-width="150px" :rules="rules" ref="formRef">
        <el-form-item label="Наименование" prop="name" required>
          <el-input v-model="form.name" placeholder="ТПА-05" />
        </el-form-item>

        <el-form-item label="Инвентарный номер">
          <el-input v-model="form.inventoryNumber" placeholder="INV-2026-005" />
        </el-form-item>

        <el-form-item label="Псевдоним коннектора">
          <el-input v-model="form.connectorAlias" placeholder="TPA-06" clearable />
          <div class="text-sm text-gray-500 mt-1">Имя машины в OPC-браузере (только для коннекторов)</div>
        </el-form-item>

        <el-form-item label="Шаблон оборудования" prop="templateId" required>
          <el-select v-model="form.templateId" placeholder="Выберите шаблон" class="w-full">
            <el-option
              v-for="template in templates"
              :key="template.id"
              :label="`${template.name} (${template.manufacturer} ${template.model})`"
              :value="template.id"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="Дата ввода в эксплуатацию">
          <el-date-picker
            v-model="form.commissioningDate"
            type="date"
            placeholder="Выберите дату"
            format="DD.MM.YYYY"
            value-format="YYYY-MM-DD"
            clearable
          />
        </el-form-item>

        <el-form-item label="Статус">
          <el-switch v-model="form.isActive" active-text="Активен" inactive-text="Не активен" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveImm" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>

    <QrCodeDialog v-model="qrDialogVisible" :qr-data="qrData" :label="qrLabel" />
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { immApi } from '@/api/imm'
import { templatesApi } from '@/api/templates'
import QrCodeDialog from '@/components/common/QrCodeDialog.vue'
import dayjs from 'dayjs'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingImm = ref(null)
const formRef = ref(null)

const imms = ref([])
const templates = ref([])

const qrDialogVisible = ref(false)
const qrData = ref('')
const qrLabel = ref('')

const showQr = async (imm) => {
  try {
    const response = await immApi.getQr(imm.id)
    qrData.value = response.data.qrData
    qrLabel.value = imm.name
    qrDialogVisible.value = true
  } catch (error) {
    ElMessage.error('Ошибка получения QR-кода')
  }
}

const filters = reactive({
  isActive: null
})

const form = reactive({
  name: '',
  inventoryNumber: '',
  connectorAlias: '',
  commissioningDate: null,
  templateId: '',
  isActive: true
})

const rules = {
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }],
  templateId: [{ required: true, message: 'Выберите шаблон', trigger: 'change' }]
}

onMounted(async () => {
  await Promise.all([loadImms(), loadTemplates()])
})

const loadImms = async () => {
  loading.value = true
  try {
    const response = await immApi.getList({ isActive: filters.isActive })
    imms.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки ТПА')
  } finally {
    loading.value = false
  }
}

const loadTemplates = async () => {
  try {
    const response = await templatesApi.getList()
    templates.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки шаблонов')
  }
}

const showCreateModal = () => {
  editingImm.value = null
  Object.assign(form, {
    name: '',
    inventoryNumber: '',
    connectorAlias: '',
    commissioningDate: null,
    templateId: '',
    isActive: true
  })
  dialogVisible.value = true
}

const editImm = (imm) => {
  editingImm.value = imm
  Object.assign(form, {
    name: imm.name,
    inventoryNumber: imm.inventoryNumber,
    connectorAlias: imm.connectorAlias ?? '',
    commissioningDate: imm.commissioningDate ? dayjs(imm.commissioningDate).format('YYYY-MM-DD') : null,
    templateId: imm.templateId,
    isActive: imm.isActive
  })
  dialogVisible.value = true
}

const saveImm = async () => {
  if (!formRef.value) return
  
  await formRef.value.validate(async (valid) => {
    if (!valid) return

    saving.value = true
    try {
      if (editingImm.value) {
        await immApi.update(editingImm.value.id, form)
        ElMessage.success('ТПА обновлён')
      } else {
        await immApi.create(form)
        ElMessage.success('ТПА создан')
      }

      dialogVisible.value = false
      await loadImms()
    } catch (error) {
      ElMessage.error('Ошибка сохранения ТПА')
    } finally {
      saving.value = false
    }
  })
}

const deleteImm = async (imm) => {
  try {
    await ElMessageBox.confirm(`Удалить ТПА "${imm.name}"?`, 'Подтверждение', {
      type: 'warning'
    })
    
    // Мягкое удаление через деактивацию
    await immApi.update(imm.id, { isActive: false })
    ElMessage.success('ТПА деактивирован')
    await loadImms()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка удаления ТПА')
    }
  }
}

const formatDate = (date, withTime = true) => {
  return dayjs(date).format(withTime ? 'DD.MM.YYYY HH:mm' : 'DD.MM.YYYY')
}
</script>