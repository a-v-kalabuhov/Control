<template>
  <el-dialog
    v-model="visible"
    title="Детали задания"
    width="800px"
    :close-on-click-modal="false"
  >
    <div v-loading="loading">
      <!-- Основная информация -->
      <el-descriptions :column="2" border class="mb-6">
        <el-descriptions-item label="Статус">
          <TaskStatusBadge :status="task?.status" />
        </el-descriptions-item>
        <el-descriptions-item label="Выдано">
          {{ formatDate(task?.issuedAt) }}
        </el-descriptions-item>
        <el-descriptions-item label="Оборудование">
          {{ task?.immName }}
        </el-descriptions-item>
        <el-descriptions-item label="Пресс-форма">
          {{ task?.moldName }}
        </el-descriptions-item>
        <el-descriptions-item label="Наладчик">
          {{ task?.personnelName || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="Начато">
          {{ formatDate(task?.startedAt) }}
        </el-descriptions-item>
        <el-descriptions-item v-if="task?.completedAt" label="Завершено">
          {{ formatDate(task?.completedAt) }}
        </el-descriptions-item>
        <el-descriptions-item v-if="task?.closedAt" label="Закрыто">
          {{ formatDate(task?.closedAt) }}
        </el-descriptions-item>
        <el-descriptions-item
          v-if="task?.closeReason"
          :label="task?.status === 'Closed' ? 'Причина закрытия' : 'Причина недовыполнения'"
          :span="2"
        >
          {{ task.closeReason }}
        </el-descriptions-item>
      </el-descriptions>

      <!-- Прогресс -->
      <el-card class="mb-6">
        <template #header>
          <span class="font-semibold">Выполнение</span>
        </template>
        <TaskProgress 
          :plan-quantity="task?.planQuantity" 
          :actual-quantity="task?.actualQuantity" 
        />
      </el-card>

      <!-- Примечания -->
      <el-card v-if="task?.note">
        <template #header>
          <span class="font-semibold">Примечания</span>
        </template>
        <p class="text-gray-800">{{ task.note }}</p>
      </el-card>

      <!-- Действия -->
      <div class="flex justify-end gap-4 mt-6" v-if="canEdit || canIssue || canComplete || canClose">
        <el-button
          v-if="canEdit"
          @click="$emit('edit', task)"
        >
          Редактировать
        </el-button>
        <el-popconfirm
          v-if="canIssue"
          title="Выдать задание в работу?"
          confirm-button-text="Выдать"
          cancel-button-text="Отмена"
          confirm-button-type="warning"
          width="240"
          @confirm="$emit('issue', task)"
        >
          <template #reference>
            <el-button type="warning">Выдать</el-button>
          </template>
        </el-popconfirm>
        <el-button
          v-if="canComplete"
          type="success"
          @click="$emit('complete', task)"
        >
          Завершить
        </el-button>
        <el-button
          v-if="canClose"
          type="info"
          @click="$emit('close', task)"
        >
          Закрыть
        </el-button>
      </div>
    </div>
  </el-dialog>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useAuthStore } from '@/stores/auth'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import TaskProgress from '@/components/tasks/TaskProgress.vue'
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

const emit = defineEmits(['update:modelValue', 'edit', 'issue', 'complete', 'close'])

const authStore = useAuthStore()
const loading = ref(false)

const visible = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value)
})

const canEdit = computed(() => {
  if (!props.task) return false
  return props.task.status === 'Draft' && authStore.isManager
})

const canIssue = computed(() => {
  if (!props.task) return false
  return props.task.status === 'Draft' && authStore.isManager
})

const canComplete = computed(() => {
  if (!props.task) return false
  return props.task.status === 'InProgress' && authStore.isAdjuster
})

const canClose = computed(() => {
  if (!props.task) return false
  return ['Completed', 'InProgress'].includes(props.task.status) && authStore.isManager
})

const formatDate = (date) => {
  if (!date) return '—'
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}
</script>