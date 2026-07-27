<template>
  <el-dialog v-model="visible" title="Карточка заказа" width="900px">
    <div v-loading="loading">
      <div class="mb-4 flex items-center gap-4">
        <span class="font-semibold text-lg">{{ order?.number }}</span>
        <span
          class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
          :class="`${getOrderStatusMeta(order?.status).bg} ${getOrderStatusMeta(order?.status).text}`"
        >
          {{ getOrderStatusMeta(order?.status).label }}
        </span>
        <span class="text-gray-500 text-sm">Срок: {{ formatDate(order?.dueDate) }}</span>
      </div>

      <el-descriptions :column="4" border class="mb-4">
        <el-descriptions-item label="Заказано">{{ order?.quantity }}</el-descriptions-item>
        <el-descriptions-item label="Выпущено">{{ order?.producedQuantity }}</el-descriptions-item>
        <el-descriptions-item label="Брак">{{ order?.defectQuantity }}</el-descriptions-item>
        <el-descriptions-item label="Годных">{{ order?.goodQuantity }}</el-descriptions-item>
      </el-descriptions>
      <el-progress :percentage="progressPercentage" class="mb-6" />

      <div class="flex justify-between items-center mb-2">
        <span class="font-semibold">Задания</span>
        <div class="flex gap-2">
          <!-- el-tooltip не получает события от disabled-кнопки — нужна обёртка -->
          <el-tooltip
            :disabled="isOrderActive"
            content="Задание можно создать только для активного заказа"
          >
            <span>
              <el-button
                size="small"
                type="primary"
                :disabled="!isOrderActive"
                @click="openCreateTask"
              >
                Создать задание
              </el-button>
            </span>
          </el-tooltip>
          <el-tooltip
            :disabled="isOrderActive"
            content="Привязать задание можно только к активному заказу"
          >
            <span>
              <el-button size="small" :disabled="!isOrderActive" @click="openAttachDialog">
                Привязать существующее
              </el-button>
            </span>
          </el-tooltip>
        </div>
      </div>
      <el-table :data="order?.tasks ?? []" stripe style="width: 100%">
        <el-table-column prop="immName" label="ТПА" />
        <el-table-column prop="moldName" label="ПФ" />
        <el-table-column prop="planQuantity" label="План" width="90" align="center" />
        <el-table-column prop="actualQuantity" label="Факт" width="90" align="center" />
        <el-table-column prop="defectQuantity" label="Брак" width="90" align="center" />
        <el-table-column label="Статус" width="120">
          <template #default="{ row }">{{ row.status }}</template>
        </el-table-column>
        <el-table-column label="" width="110" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="danger" @click="detach(row)">Отвязать</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <el-dialog v-model="attachDialogVisible" title="Привязать задание" width="480px" append-to-body>
      <el-select
        v-model="selectedTaskId"
        filterable
        placeholder="Выберите задание"
        class="w-full"
        :loading="attachLoading"
      >
        <el-option
          v-for="t in attachableTasks"
          :key="t.id"
          :label="`${t.immName} / ${t.moldName}`"
          :value="t.id"
        />
      </el-select>
      <template #footer>
        <el-button @click="attachDialogVisible = false">Отмена</el-button>
        <el-button type="primary" :disabled="!selectedTaskId" @click="attach">Привязать</el-button>
      </template>
    </el-dialog>

    <TaskFormModal
      v-if="lockedOrder"
      v-model="taskFormVisible"
      :locked-order="lockedOrder"
      append-to-body
      @success="onTaskCreated"
    />
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { ordersApi } from '@/api/orders'
import { tasksApi } from '@/api/tasks'
import { moldsApi } from '@/api/molds'
import { getOrderStatusMeta } from '@/constants/orderStatus'
import { apiErrorMessage } from '@/utils/apiError'
import TaskFormModal from '@/views/tasks/TaskFormModal.vue'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  orderId: {
    type: String,
    default: null
  }
})

const emit = defineEmits(['update:modelValue', 'updated'])

const loading = ref(false)
const order = ref(null)
const attachDialogVisible = ref(false)
const attachLoading = ref(false)
const attachableTasks = ref([])
const selectedTaskId = ref(null)
const taskFormVisible = ref(false)

const visible = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value)
})

const progressPercentage = computed(() => {
  if (!order.value) return 0
  return Math.min(100, Math.round(order.value.progressPercent ?? 0))
})

const isOrderActive = computed(() => order.value?.status === 'Active')

// Всё, что форме задания нужно знать о заказе: отдельный запрос ей не потребуется.
const lockedOrder = computed(() =>
  order.value
    ? {
        id: order.value.id,
        number: order.value.number,
        productTypeId: order.value.productTypeId,
        productTypeArticle: order.value.productTypeArticle
      }
    : null
)

watch(
  () => props.modelValue,
  (isOpen) => {
    if (isOpen && props.orderId) loadOrder()
  },
  { immediate: true }
)

function formatDate(date) {
  return date ? dayjs(date).format('DD.MM.YYYY') : '—'
}

async function loadOrder() {
  loading.value = true
  try {
    const { data } = await ordersApi.getById(props.orderId)
    order.value = data
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка загрузки заказа'))
  } finally {
    loading.value = false
  }
}

async function detach(row) {
  try {
    await ordersApi.detachTask(props.orderId, row.taskId)
    ElMessage.success('Задание отвязано')
    await loadOrder()
    emit('updated')
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка отвязки задания'))
  }
}

async function openAttachDialog() {
  selectedTaskId.value = null
  attachDialogVisible.value = true
  attachLoading.value = true
  try {
    const [tasksRes, moldsRes] = await Promise.all([tasksApi.getList(), moldsApi.getList()])
    const productTypeByMoldId = new Map(moldsRes.data.map((m) => [m.id, m.productTypeId]))
    attachableTasks.value = tasksRes.data.filter(
      (t) => !t.orderId && productTypeByMoldId.get(t.moldId) === order.value?.productTypeId
    )
  } catch (error) {
    attachableTasks.value = []
    ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить задания для привязки'))
  } finally {
    attachLoading.value = false
  }
}

async function attach() {
  if (!selectedTaskId.value) return
  try {
    await ordersApi.attachTask(props.orderId, selectedTaskId.value)
    ElMessage.success('Задание привязано')
    attachDialogVisible.value = false
    await loadOrder()
    emit('updated')
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка привязки задания'))
  }
}

function openCreateTask() {
  taskFormVisible.value = true
}

async function onTaskCreated() {
  await loadOrder()
  emit('updated')
}
</script>
