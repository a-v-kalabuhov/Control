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
            <el-option label="Наладка" value="Setup" />
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
      element-loading-text="Загрузка заданий..."
      class="space-y-3 min-h-[80px]"
    >
      <MobileTaskCard
        v-for="task in mobileStore.filteredTasks"
        :key="task.id"
        :task="task"
        @click="openTaskDetail(task)"
        @start="startTask(task)"
        @complete-setup="completeSetup(task)"
        @cancel-setup="cancelSetup(task)"
        @complete="completeTask(task)"
      />
    </div>

    <div v-if="!mobileStore.loading && mobileStore.filteredTasks.length === 0" class="text-center py-12">
      <el-empty description="Заданий нет" />
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
      @complete-setup="completeSetup"
      @cancel-setup="cancelSetup"
      @complete="completeTask"
      @close="closeTask"
    />

    <!-- Модальное окно сканера -->
    <el-dialog
      v-model="scannerVisible"
      title="Сканирование QR-кода"
      width="90%"
      :close-on-click-modal="false"
      @closed="scanResult = null"
    >
      <!-- Результат сканирования -->
      <div v-if="scanResult" class="py-4 text-center space-y-4">
        <el-icon
          :size="56"
          :color="scanResult.matched === true ? '#67c23a' : scanResult.matched === false ? '#f56c6c' : '#409eff'"
        >
          <CircleCheck v-if="scanResult.matched === true" />
          <CircleClose v-else-if="scanResult.matched === false" />
          <InfoFilled v-else />
        </el-icon>
        <div class="space-y-1">
          <p
            v-for="(line, i) in scanResult.lines"
            :key="i"
            :class="i === 0 ? 'text-base font-semibold text-gray-800' : 'text-sm text-gray-500'"
          >{{ line }}</p>
        </div>
        <div class="flex gap-2 pt-2">
          <el-button class="flex-1" @click="scanResult = null">Сканировать ещё</el-button>
          <el-button class="flex-1" type="primary" @click="scannerVisible = false">Закрыть</el-button>
        </div>
      </div>

      <!-- Сам сканер -->
      <QrScanner
        v-else
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
import { mobileApi } from '@/api/mobile'
import MobileTaskCard from '@/components/mobile/MobileTaskCard.vue'
import MobileTaskDetailView from './MobileTaskDetailView.vue'
import QrScanner from '@/components/mobile/QrScanner.vue'

const mobileStore = useMobileStore()

const detailVisible = ref(false)
const scannerVisible = ref(false)
const selectedTask = ref(null)
const scanResult = ref(null)

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
    await mobileApi.startSetup(task.id)
    ElMessage.success('Наладка начата')
    await loadTasks()
    detailVisible.value = false
  } catch (error) {
    ElMessage.error('Ошибка начала наладки')
  }
}

const completeSetup = async (task) => {
  try {
    if (!task.moldVerifiedAt) {
      await ElMessageBox.confirm(
        'Пресс-форма не была отсканирована. Завершить наладку без верификации?',
        'Предупреждение',
        { type: 'warning', confirmButtonText: 'Завершить', cancelButtonText: 'Отмена' }
      )
    }

    await mobileApi.completeSetup(task.id)
    ElMessage.success('Наладка завершена, задание в работе')
    await loadTasks()
    detailVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка завершения наладки')
    }
  }
}

const cancelSetup = async (task) => {
  try {
    await ElMessageBox.confirm(
      'Отменить наладку? Задание вернётся в статус «Выдано».',
      'Подтверждение',
      { type: 'warning', confirmButtonText: 'Отменить наладку', cancelButtonText: 'Назад' }
    )

    await mobileApi.cancelSetup(task.id)
    ElMessage.info('Наладка отменена')
    await loadTasks()
    detailVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка отмены наладки')
    }
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
  scanResult.value = null
  scannerVisible.value = true
}

const handleScanConfirm = async (qrData) => {
  try {
    const parsed = JSON.parse(qrData)
    const setupTask = mobileStore.setupTask

    if (parsed.entity === 'mold') {
      if (setupTask && parsed.id === setupTask.moldId) {
        await mobileApi.verifyMold(setupTask.id)
        await loadTasks()
        if (selectedTask.value?.id === setupTask.id) {
          selectedTask.value = mobileStore.tasks.find(t => t.id === setupTask.id) ?? selectedTask.value
        }
        scanResult.value = { matched: true, lines: ['Пресс-форма из задания обнаружена', setupTask.moldName] }
      } else if (setupTask) {
        scanResult.value = {
          matched: false,
          lines: [
            'Пресс-форма не совпадает с заданием',
            `Отсканировано: ${parsed.name || parsed.id}`,
            `Ожидалась: ${setupTask.moldName}`
          ]
        }
      } else {
        scanResult.value = { matched: null, lines: [`Пресс-форма: ${parsed.name || parsed.id}`] }
      }
    } else if (parsed.entity === 'machine') {
      scanResult.value = { matched: null, lines: [`ТПА: ${parsed.name || parsed.id}`] }
    } else {
      scanResult.value = { matched: null, lines: [`Объект: ${parsed.name || parsed.id || qrData}`] }
    }
  } catch {
    scanResult.value = { matched: false, lines: ['Ошибка обработки QR-кода'] }
  }
}

const handleScanError = (error) => {
  ElMessage.error('Ошибка сканирования: ' + error.message)
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
