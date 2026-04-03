<template>
  <el-dialog
    v-model="visible"
    title="Задание"
    width="95%"
    :close-on-click-modal="false"
    class="mobile-task-dialog"
  >
    <div v-loading="loading">
      <!-- Статус -->
      <div class="mb-4 text-center">
        <MobileStatusBadge :status="task?.status" />
      </div>

      <!-- Информация -->
      <el-descriptions :column="1" border class="mb-4">
        <el-descriptions-item label="Оборудование">
          <div class="text-lg font-bold">{{ task?.immName }}</div>
        </el-descriptions-item>
        <el-descriptions-item label="Пресс-форма">
          <div class="text-base">{{ task?.moldName }}</div>
        </el-descriptions-item>
        <el-descriptions-item label="План">
          <div class="text-xl font-bold text-primary-600">{{ task?.planQuantity }} шт</div>
        </el-descriptions-item>
        <el-descriptions-item label="Факт">
          <div class="text-xl font-bold" :class="progressColor">{{ task?.actualQuantity || 0 }} шт</div>
        </el-descriptions-item>
        <el-descriptions-item label="Прогресс">
          <el-progress 
            :percentage="progressPercent" 
            :stroke-width="16"
            :status="progressStatus"
          />
        </el-descriptions-item>
        <el-descriptions-item label="Выдано">
          {{ formatDate(task?.issuedAt) }}
        </el-descriptions-item>
        <el-descriptions-item label="Наладчик">
          {{ task?.personnelName || '—' }}
        </el-descriptions-item>
      </el-descriptions>

      <!-- Примечание -->
      <el-alert
        v-if="task?.note"
        title="Примечание"
        type="info"
        :closable="false"
        class="mb-4"
      >
        {{ task.note }}
      </el-alert>

      <!-- Кнопки действий -->
      <div class="grid grid-cols-2 gap-3">
        <el-button 
          v-if="task?.status === 'Issued'"
          type="success" 
          size="large"
          class="h-14 text-lg"
          @click="$emit('start', task)"
        >
          <el-icon class="mr-1"><VideoPlay /></el-icon>
          Начать
        </el-button>

        <el-button 
          v-if="task?.status === 'InProgress'"
          type="primary" 
          size="large"
          class="h-14 text-lg"
          @click="$emit('complete', task)"
        >
          <el-icon class="mr-1"><CircleCheck /></el-icon>
          Завершить
        </el-button>

        <el-button 
          v-if="task?.status === 'InProgress'"
          type="warning" 
          size="large"
          class="h-14 text-lg"
          @click="openDowntime"
        >
          <el-icon class="mr-1"><Clock /></el-icon>
          Простой
        </el-button>

        <el-button 
          v-if="canClose"
          type="info" 
          size="large"
          class="h-14 text-lg"
          @click="$emit('close', task)"
        >
          <el-icon class="mr-1"><DocumentDelete /></el-icon>
          Закрыть
        </el-button>
      </div>
    </div>
  </el-dialog>

  <!-- Диалог простоя -->
  <el-dialog
    v-model="downtimeVisible"
    title="Простой"
    width="95%"
  >
    <el-form label-position="top">
      <el-form-item label="Причина простоя">
        <el-select v-model="downtimeReason" class="w-full" size="large">
          <el-option
            v-for="reason in mobileStore.downtimeReasons"
            :key="reason.id"
            :label="reason.name"
            :value="reason.id"
          />
        </el-select>
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button 
        v-if="!mobileStore.activeDowntime"
        type="warning" 
        size="large"
        class="w-full h-12"
        @click="startDowntime"
      >
        Начать простой
      </el-button>
      <el-button 
        v-else
        type="success" 
        size="large"
        class="w-full h-12"
        @click="stopDowntime"
      >
        Завершить простой
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { useMobileStore } from '@/stores/mobile'
import { useAuthStore } from '@/stores/auth'
import MobileStatusBadge from '@/components/mobile/MobileStatusBadge.vue'
import dayjs from 'dayjs'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  task: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['update:modelValue', 'start', 'complete', 'close'])

const mobileStore = useMobileStore()
const authStore = useAuthStore()

const visible = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value)
})

const loading = ref(false)
const downtimeVisible = ref(false)
const downtimeReason = ref(null)

const progressPercent = computed(() => {
  if (!props.task?.planQuantity || props.task.planQuantity === 0) return 0
  return Math.min(100, Math.round((props.task.actualQuantity / props.task.planQuantity) * 100))
})

const progressStatus = computed(() => {
  if (progressPercent.value >= 100) return 'success'
  if (progressPercent.value >= 75) return ''
  if (progressPercent.value >= 50) return 'warning'
  return 'exception'
})

const progressColor = computed(() => {
  if (progressPercent.value >= 100) return 'text-green-600'
  if (progressPercent.value >= 75) return 'text-blue-600'
  if (progressPercent.value >= 50) return 'text-yellow-600'
  return 'text-red-600'
})

const canClose = computed(() => {
  return ['Completed', 'InProgress'].includes(props.task?.status) && 
         (authStore.isManager || authStore.isAdmin)
})

const formatDate = (date) => {
  if (!date) return '—'
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}

const openDowntime = () => {
  downtimeReason.value = null
  downtimeVisible.value = true
}

const startDowntime = async () => {
  if (!downtimeReason.value) {
    ElMessage.warning('Выберите причину простоя')
    return
  }

  const result = await mobileStore.startDowntime(props.task.immId, downtimeReason.value)
  if (result.success) {
    downtimeVisible.value = false
  }
}

const stopDowntime = async () => {
  const result = await mobileStore.stopDowntime(props.task.immId)
  if (result.success) {
    downtimeVisible.value = false
  }
}
</script>

<style scoped>
.mobile-task-dialog :deep(.el-dialog__body) {
  @apply p-4;
}

.mobile-task-dialog :deep(.el-button) {
  @apply min-h-[48px];
}

.mobile-task-dialog :deep(.el-input__wrapper),
.mobile-task-dialog :deep(.el-select .el-input__wrapper) {
  @apply min-h-[48px];
}
</style>