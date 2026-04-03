<template>
  <div class="mobile-tasks-view">
    <!-- Заголовок -->
    <div class="mb-4">
      <h2 class="text-xl font-bold text-gray-800">Мои задания</h2>
      <p class="text-sm text-gray-600 mt-1">
        {{ mobileStore.activeTasks.length }} активных | 
        {{ mobileStore.completedTasks.length }} завершённых
      </p>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true" class="mobile-filters">
        <el-form-item>
          <el-input 
            v-model="mobileStore.filters.search" 
            placeholder="Поиск ТПА, ПФ"
            clearable
            prefix-icon="Search"
            class="w-full"
            @input="loadTasks"
          />
        </el-form-item>
        <el-form-item>
          <el-select 
            v-model="mobileStore.filters.status" 
            placeholder="Все статусы"
            clearable
            class="w-full"
            @change="loadTasks"
          >
            <el-option label="Выдано" value="Issued" />
            <el-option label="В работе" value="InProgress" />
            <el-option label="Выполнено" value="Completed" />
            <el-option label="Закрыто" value="Closed" />
          </el-select>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Список заданий -->
    <div 
      v-loading="mobileStore.loading"
      class="space-y-3"
    >
      <div v-if="mobileStore.filteredTasks.length === 0" class="text-center py-12">
        <el-empty description="Заданий нет" />
      </div>

      <MobileTaskCard
        v-for="task in mobileStore.filteredTasks"
        :key="task.id"
        :task="task"
        @click="openTaskDetail(task)"
        @start="startTask(task)"
        @complete="completeTask(task)"
      />
    </div>

    <!-- Кнопка сканера -->
    <el-button 
      type="primary" 
      size="large"
      class="fixed bottom-6 right-6 w-16 h-16 rounded-full shadow-lg text-2xl"
      @click="openScanner"
    >
      <el-icon><Scan /></el-icon>
    </el-button>

    <!-- Модальное окно деталей -->
    <MobileTaskDetailView
      v-model="detailVisible"
      :task="selectedTask"
      @start="startTask"
      @complete="completeTask"
      @close="closeTask"
    />

    <!-- Модальное окно сканера -->
    <el-dialog
      v-model="scannerVisible"
      title="Сканирование QR-кода"
      width="90%"
      :close-on-click-modal="false"
      @closed="onScannerClosed"
    >
      <QrScanner
        @confirm="handleScanConfirm"
        @cancel="scannerVisible = false"
        @error="handleScanError"
      />
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useMobileStore } from '@/stores/mobile'
import { useAuthStore } from '@/stores/auth'
import { mobileApi } from '@/api/mobile'
import MobileTaskCard from '@/components/mobile/MobileTaskCard.vue'
import MobileTaskDetailView from './MobileTaskDetailView.vue'
import QrScanner from '@/components/mobile/QrScanner.vue'

const mobileStore = useMobileStore()
const authStore = useAuthStore()

const detailVisible = ref(false)
const scannerVisible = ref(false)
const selectedTask = ref(null)
const scanContext = ref(null) // 'start-task', 'complete-task', 'downtime'

onMounted(async () => {
  await loadTasks()
  await mobileStore.loadDowntimeReasons()
})

const loadTasks = async () => {
  await mobileStore.loadMyTasks()
}

const openTaskDetail = (task) => {
  selectedTask.value = task
  detailVisible.value = true
}

const startTask = async (task) => {
  try {
    // Требуем сканирование QR перед началом
    scanContext.value = 'start-task'
    selectedTask.value = task
    scannerVisible.value = true
  } catch (error) {
    ElMessage.error('Ошибка начала задания')
  }
}

const completeTask = async (task) => {
  try {
    const { value } = await ElMessageBox.prompt(
      'Укажите фактическое количество (если отличается от плана)',
      'Завершение задания',
      {
        inputPattern: /^\d+$/,
        inputErrorMessage: 'Введите число',
        inputValue: task.planQuantity
      }
    )

    const actualQty = parseInt(value)
    let completionReason = ''

    if (actualQty < task.planQuantity) {
      const { value: reason } = await ElMessageBox.prompt(
        'Укажите причину невыполнения плана',
        'Причина',
        {
          inputPattern: /.+/,
          inputErrorMessage: 'Введите причину'
        }
      )
      completionReason = reason
    }

    await mobileApi.completeTask(task.id, {
      actualQuantity: actualQty,
      completionReason
    })

    ElMessage.success('Задание завершено')
    await loadTasks()
    detailVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка завершения задания')
    }
  }
}

const closeTask = async (task) => {
  try {
    await ElMessageBox.confirm('Закрыть задание?', 'Подтверждение', {
      type: 'warning'
    })

    await mobileApi.closeTask(task.id, {})
    ElMessage.success('Задание закрыто')
    await loadTasks()
    detailVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка закрытия задания')
    }
  }
}

const openScanner = () => {
  scanContext.value = 'manual-scan'
  scannerVisible.value = true
}

const handleScanConfirm = async (qrData) => {
  scannerVisible.value = false

  try {
    const parsed = JSON.parse(qrData)
    
    if (scanContext.value === 'start-task' && selectedTask.value) {
      // Валидация QR
      if (parsed.entity === 'mold' && parsed.id === selectedTask.value.moldId) {
        await mobileApi.startTask(selectedTask.value.id, {
          moldQr: qrData,
          immQr: '' // TODO: Сканировать QR ТПА
        })
        ElMessage.success('Задание начато')
        await loadTasks()
        detailVisible.value = false
      } else if (parsed.entity === 'machine' && parsed.id === selectedTask.value.immId) {
        await mobileApi.startTask(selectedTask.value.id, {
          moldQr: '',
          immQr: qrData
        })
        ElMessage.success('Задание начато')
        await loadTasks()
        detailVisible.value = false
      } else {
        ElMessage.warning('QR-код не соответствует заданию')
      }
    } else {
      ElMessage.success('QR распознан: ' + parsed.entity)
    }
  } catch (error) {
    ElMessage.error('Ошибка обработки QR-кода')
  }
}

const handleScanError = (error) => {
  ElMessage.error('Ошибка сканирования: ' + error.message)
}

const onScannerClosed = () => {
  scanContext.value = null
  selectedTask.value = null
}
</script>

<style scoped>
.mobile-tasks-view {
  @apply p-4 pb-24;
}

.mobile-filters :deep(.el-form-item) {
  @apply w-full mb-3;
}

.mobile-filters :deep(.el-input),
.mobile-filters :deep(.el-select) {
  @apply w-full;
}
</style>