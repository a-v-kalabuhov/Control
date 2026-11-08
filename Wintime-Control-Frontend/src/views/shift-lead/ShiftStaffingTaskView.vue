<template>
  <div v-loading="store.loading" class="min-h-[160px]">
    <el-button class="mb-4" text type="primary" @click="router.push(`/shift-lead/items/${itemId}`)">
      ← К смене
    </el-button>

    <template v-if="task">
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
        <TaskResultsTab :task-id="taskId" :staffing-item-id="itemId" />
      </div>
    </template>

    <el-empty v-else-if="!store.loading" description="Задание не найдено в этой смене" />
  </div>
</template>

<script setup>
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import TaskResultsTab from '@/components/tasks/TaskResultsTab.vue'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'

const route = useRoute()
const router = useRouter()
const store = useShiftStaffingStore()
const itemId = String(route.params.itemId)
const taskId = String(route.params.taskId)

const imm = computed(() => store.currentItem?.imms.find(item =>
  item.tasks.some(candidate => String(candidate.id) === taskId)))
const task = computed(() => imm.value?.tasks.find(candidate => String(candidate.id) === taskId))

onMounted(() => {
  if (store.currentItem?.shift?.itemId !== itemId) return store.loadItem(itemId)
})

function secondsLabel(value) {
  return value == null ? '—' : `${value} с`
}
</script>
