<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник причин простоев</h2>
        <p class="text-gray-600 mt-1">Классификатор причин остановок оборудования</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить причину
      </el-button>
    </div>

    <!-- Таблица -->
    <el-table :data="reasons" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="name" label="Наименование" />
      <el-table-column prop="type" label="Тип" width="150">
        <template #default="{ row }">
          <el-tag :type="row.type === 'Planned' ? 'success' : 'danger'">
            {{ row.type === 'Planned' ? 'Плановый' : 'Аварийный' }}
          </el-tag>
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
          <el-button size="small" @click="editReason(row)">Редактировать</el-button>
          <el-button size="small" type="danger" @click="deleteReason(row)">Удалить</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- Модальное окно -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingReason ? 'Редактирование' : 'Новая причина'"
      width="500px"
    >
      <el-form :model="form" label-width="120px" :rules="rules" ref="formRef">
        <el-form-item label="Наименование" prop="name" required>
          <el-input v-model="form.name" placeholder="Наладка, ППР, Авария" />
        </el-form-item>

        <el-form-item label="Тип" prop="type" required>
          <el-radio-group v-model="form.type">
            <el-radio label="Planned">Плановый</el-radio>
            <el-radio label="Emergency">Аварийный</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item label="Статус">
          <el-switch v-model="form.isActive" active-text="Активна" inactive-text="Не активна" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveReason" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { downtimeApi } from '@/api/downtime'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingReason = ref(null)
const formRef = ref(null)

const reasons = ref([])

const form = reactive({
  name: '',
  type: 'Planned',
  isActive: true
})

const rules = {
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }],
  type: [{ required: true, message: 'Выберите тип', trigger: 'change' }]
}

onMounted(async () => {
  await loadReasons()
})

const loadReasons = async () => {
  loading.value = true
  try {
    const response = await downtimeApi.getReasons({ isActive: true })
    reasons.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки причин простоев')
  } finally {
    loading.value = false
  }
}

const showCreateModal = () => {
  editingReason.value = null
  Object.assign(form, {
    name: '',
    type: 'Planned',
    isActive: true
  })
  dialogVisible.value = true
}

const editReason = (reason) => {
  editingReason.value = reason
  Object.assign(form, {
    name: reason.name,
    type: reason.type,
    isActive: reason.isActive
  })
  dialogVisible.value = true
}

const saveReason = async () => {
  if (!formRef.value) return
  
  await formRef.value.validate(async (valid) => {
    if (!valid) return

    saving.value = true
    try {
      if (editingReason.value) {
        await downtimeApi.updateReason(editingReason.value.id, form)
        ElMessage.success('Причина обновлена')
      } else {
        await downtimeApi.createReason(form)
        ElMessage.success('Причина создана')
      }

      dialogVisible.value = false
      await loadReasons()
    } catch (error) {
      ElMessage.error('Ошибка сохранения причины')
    } finally {
      saving.value = false
    }
  })
}

const deleteReason = async (reason) => {
  try {
    await ElMessageBox.confirm(`Удалить причину "${reason.name}"?`, 'Подтверждение', {
      type: 'warning'
    })
    
    await downtimeApi.deleteReason(reason.id)
    ElMessage.success('Причина удалена')
    await loadReasons()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка удаления причины')
    }
  }
}
</script>