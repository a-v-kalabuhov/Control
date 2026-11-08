<template>
  <div v-loading="store.loading" class="min-h-[160px]">
    <el-button class="mb-4" text type="primary" @click="router.push(`/shift-lead/items/${itemId}`)">
      ← К смене
    </el-button>

    <el-alert
      v-if="loadError"
      data-test="item-load-error"
      type="error"
      :title="loadError"
      :closable="false"
    >
      <el-button size="small" data-test="retry-item-load" @click="loadCurrentItem">
        Повторить
      </el-button>
    </el-alert>

    <template v-else-if="task">
      <div class="mb-4 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 class="text-xl font-bold text-gray-800">{{ task.productFullArticle || 'Задание' }}</h2>
            <p v-if="task.productName" class="mt-1 text-gray-600">{{ task.productName }}</p>
          </div>
          <TaskStatusBadge :status="task.status" />
        </div>

        <dl class="mt-4 grid grid-cols-1 gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
          <div>
            <dt class="text-gray-500">ТПА</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ task.immName || imm?.name || '—' }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">Пресс-форма</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ task.moldName || '—' }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">План</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ task.planQuantity }} шт.</dd>
          </div>
          <div>
            <dt class="text-gray-500">Выпущено</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ task.actualQuantity || 0 }} шт.</dd>
          </div>
          <div>
            <dt class="text-gray-500">Брак</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ task.defectQuantity || 0 }} шт.</dd>
          </div>
          <div>
            <dt class="text-gray-500">Наладчик</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ task.personnelName || '—' }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">Полный цикл, эталон</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ secondsLabel(task.plannedFullCycleSeconds) }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">Цикл литья, эталон</dt>
            <dd class="mt-1 font-medium text-gray-800">{{ secondsLabel(task.plannedInjectionCycleSeconds) }}</dd>
          </div>
        </dl>
      </div>

      <div class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <h3 class="mb-4 text-lg font-semibold text-gray-800">Итоги задания</h3>
        <TaskResultsTab
          :key="`${itemId}/${taskId}`"
          :task-id="taskId"
          :staffing-item-id="itemId"
        />
      </div>
    </template>

    <el-empty v-else-if="!store.loading && !loadError" description="Задание не найдено в этой смене" />
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import TaskResultsTab from '@/components/tasks/TaskResultsTab.vue'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'

const route = useRoute()
const router = useRouter()
const store = useShiftStaffingStore()
const itemId = computed(() => String(route.params.itemId))
const taskId = computed(() => String(route.params.taskId))
const currentItem = computed(() => {
  const item = store.currentItem
  return item?.shift?.itemId === itemId.value ? item : null
})
const loadError = ref('')
let loadSeq = 0
let unmounted = false

const imm = computed(() => currentItem.value?.imms.find(item =>
  item.tasks.some(candidate => String(candidate.id) === taskId.value)))
const task = computed(() => imm.value?.tasks.find(candidate => String(candidate.id) === taskId.value))

store.activateItem(itemId.value)

onMounted(() => {
  if (!currentItem.value) loadCurrentItem()
})

onUnmounted(() => {
  unmounted = true
  loadSeq += 1
})

watch(itemId, (id, previousId) => {
  if (id === previousId) return
  loadError.value = ''
  store.activateItem(id)
  loadCurrentItem()
})

watch(taskId, (id, previousId) => {
  if (id !== previousId && loadError.value) loadCurrentItem()
})

async function loadCurrentItem() {
  const id = itemId.value
  const seq = ++loadSeq
  loadError.value = ''
  const result = await store.loadItem(id)
  if (unmounted || seq !== loadSeq || id !== itemId.value || result.stale) return
  if (!result.success) loadError.value = result.message || 'Не удалось загрузить смену'
}

function secondsLabel(value) {
  return value == null ? '—' : `${value} с`
}
</script>
