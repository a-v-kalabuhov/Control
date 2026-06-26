<template>
  <div class="mobile-tasks-view">
    <!-- Заголовок -->
    <div class="mb-4">
      <h2 class="text-xl font-bold text-gray-800">Мои задания</h2>
    </div>

    <!-- Поиск -->
    <el-input
      v-model="searchInput"
      placeholder="Поиск по ТПА или пресс-форме"
      clearable
      size="large"
      prefix-icon="Search"
      class="mb-4"
      @input="onSearchInput"
      @clear="onSearchInput"
    />

    <div
      v-loading="mobileStore.loading"
      element-loading-text="Загрузка заданий..."
      class="min-h-[120px]"
    >
      <!-- ─── Текущая смена ─── -->
      <section class="mb-6">
        <h3 class="section-title">
          Текущая смена
          <span v-if="mobileStore.currentShiftTasks.length" class="section-count">
            {{ mobileStore.currentShiftTasks.length }}
          </span>
        </h3>

        <div v-if="mobileStore.currentShiftTasks.length" class="space-y-3">
          <MobileTaskCard
            v-for="task in mobileStore.currentShiftTasks"
            :key="task.id"
            :task="task"
            @click="openTaskDetail(task)"
            @start="startTask(task)"
            @complete-setup="completeSetup(task)"
            @cancel-setup="cancelSetup(task)"
            @complete="completeTask(task)"
          />
        </div>
        <el-empty v-else description="Нет заданий" :image-size="80" />
      </section>

      <!-- ─── Незавершённые (с прошедших смен) ─── -->
      <section v-if="mobileStore.unfinishedTasks.length" class="mb-6">
        <h3 class="section-title section-title--warning">
          Незавершённые
          <span class="section-count section-count--warning">
            {{ mobileStore.unfinishedTasks.length }}
          </span>
        </h3>

        <div class="space-y-3">
          <MobileTaskCard
            v-for="task in mobileStore.unfinishedTasks"
            :key="task.id"
            :task="task"
            @click="openTaskDetail(task)"
            @start="startTask(task)"
            @complete-setup="completeSetup(task)"
            @cancel-setup="cancelSetup(task)"
            @complete="completeTask(task)"
          />
        </div>
      </section>

      <!-- ─── Архив заданий ─── -->
      <section class="mb-6">
        <h3 class="section-title">
          Архив заданий
          <span v-if="mobileStore.archiveTotal" class="section-count">
            {{ mobileStore.archiveTotal }}
          </span>
        </h3>

        <template v-if="mobileStore.archiveTasks.length">
          <div class="space-y-3">
            <MobileTaskCard
              v-for="task in mobileStore.archiveTasks"
              :key="task.id"
              :task="task"
              @click="openTaskDetail(task)"
            />
          </div>

          <el-pagination
            v-if="mobileStore.archiveTotal > mobileStore.archivePageSize"
            class="mt-4 justify-center"
            background
            layout="prev, pager, next"
            :pager-count="5"
            :total="mobileStore.archiveTotal"
            :page-size="mobileStore.archivePageSize"
            :current-page="mobileStore.archivePage"
            @current-change="mobileStore.setArchivePage"
          />
        </template>
        <el-empty v-else description="Архив пуст" :image-size="80" />
      </section>
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

const searchInput = ref('')
let searchTimer = null

onMounted(async () => {
  await mobileStore.loadShifts()
  await loadTasks()
  await mobileStore.loadDowntimeReasons()
})

const loadTasks = async () => {
  await mobileStore.loadMyTasks()
}

const onSearchInput = () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    mobileStore.applySearch(searchInput.value.trim())
  }, 350)
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

.section-title {
  @apply flex items-center gap-2 text-base font-bold text-gray-700 mb-3;
}

.section-title--warning {
  @apply text-orange-600;
}

.section-count {
  @apply inline-flex items-center justify-center min-w-[1.5rem] h-6 px-2
         text-sm font-semibold rounded-full bg-gray-200 text-gray-600;
}

.section-count--warning {
  @apply bg-orange-100 text-orange-600;
}

/* Крупнее для пальца на планшете */
.mobile-tasks-view :deep(.el-pagination.is-background .el-pager li),
.mobile-tasks-view :deep(.el-pagination.is-background .btn-prev),
.mobile-tasks-view :deep(.el-pagination.is-background .btn-next) {
  min-width: 40px;
  height: 40px;
  line-height: 40px;
  font-size: 16px;
}
</style>
