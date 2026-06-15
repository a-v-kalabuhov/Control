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
          v-if="canClose || (task?.closeReason && task?.status === 'Closed')"
          :label="canClose ? 'Причина закрытия досрочно' : 'Причина закрытия'"
          :span="2"
        >
          <el-input
            v-if="canClose"
            v-model="closeReasonInput"
            type="textarea"
            :rows="2"
            placeholder="Укажите причину закрытия задания"
          />
          <span v-else>{{ task.closeReason }}</span>
        </el-descriptions-item>
        <el-descriptions-item
          v-if="task?.closeReason && task?.status === 'Completed'"
          label="Причина отклонения от плана"
          :span="2"
        >
          <span>{{ task.closeReason }}</span>
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
      <div class="flex justify-between mt-6">
        <el-button @click="visible = false">Назад</el-button>
        <div class="flex gap-4">
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
            type="danger"
            :disabled="!closeReasonInput.trim()"
            @click="$emit('close', task, closeReasonInput.trim())"
          >
            Закрыть задание
          </el-button>
        </div>
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
const closeReasonInput = ref('')

const visible = computed({
  get: () => props.modelValue,
  set: (value) => {
    if (!value) closeReasonInput.value = ''
    emit('update:modelValue', value)
  }
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