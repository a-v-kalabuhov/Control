<template>
  <div v-loading="store.loading" class="min-h-[160px]">
    <template v-if="store.currentItem">
      <div class="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 class="text-xl font-bold text-gray-800">
            Смена {{ store.currentItem.shift.position }} · {{ formatDate(store.currentItem.shift.shiftDate) }}
          </h2>
          <p class="mt-1 text-sm text-gray-600">
            {{ formatDateTime(store.currentItem.shift.startAtUtc) }} —
            {{ formatDateTime(store.currentItem.shift.endAtUtc) }}
          </p>
        </div>
        <div class="flex shrink-0 gap-2">
          <el-button
            type="primary"
            data-test="nearest-report"
            :disabled="store.loading"
            @click="openReport(store.currentItem.shift.itemId)"
          >
            Сменный рапорт
          </el-button>
          <el-button
            data-test="refresh-nearest"
            :loading="store.refreshing"
            :disabled="store.loading"
            @click="refresh"
          >
            Обновить
          </el-button>
        </div>
      </div>

      <div v-if="store.currentItem.imms.length" class="space-y-3">
        <button
          v-for="imm in store.currentItem.imms"
          :key="imm.immId"
          type="button"
          :data-test="`home-imm-${imm.immId}`"
          class="w-full rounded-lg border border-gray-200 bg-white p-4 text-left shadow-sm transition hover:border-primary-400 hover:shadow"
          @click="openItem"
        >
          <div class="flex items-start justify-between gap-3">
            <div>
              <div class="text-lg font-semibold text-gray-800">{{ imm.name }}</div>
              <div v-if="imm.inventoryNumber" class="text-sm text-gray-500">{{ imm.inventoryNumber }}</div>
            </div>
            <span
              class="inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-medium"
              :class="[statusMeta(imm.effectiveStatus).bg, statusMeta(imm.effectiveStatus).text]"
            >
              <span class="h-2 w-2 rounded-full" :class="statusMeta(imm.effectiveStatus).dot" />
              {{ statusMeta(imm.effectiveStatus).label }}
            </span>
          </div>

          <div class="mt-3 text-sm font-medium text-gray-700">
            Литейщики: {{ imm.activeOperatorCount }}/2
          </div>

          <div v-if="imm.tasks.length" class="mt-3 space-y-2">
            <div
              v-for="task in imm.tasks"
              :key="task.id"
              class="rounded bg-gray-50 px-3 py-2 text-sm"
            >
              <div class="flex flex-wrap items-center justify-between gap-2">
                <span class="font-medium text-gray-800">{{ task.productFullArticle || task.moldName }}</span>
                <span class="text-gray-600">{{ taskStatusLabel(task.status) }}</span>
              </div>
              <div class="mt-1 text-gray-600">
                {{ task.actualQuantity || 0 }} / {{ task.planQuantity }} шт.
              </div>
            </div>
          </div>
          <p v-else class="mt-3 text-sm text-gray-500">Задание отсутствует</p>
        </button>
      </div>
      <el-empty v-else description="ТПА на эту смену не назначены" />
    </template>

    <section
      v-if="pendingItems.length"
      v-loading="pendingLoading"
      data-test="pending-closures"
      class="mt-6 rounded-lg border border-amber-200 bg-amber-50 p-4"
    >
      <h2 class="text-lg font-bold text-gray-800">Требует закрытия</h2>
      <p class="mt-1 text-sm text-gray-600">Завершённые смены с незакрытым сменным рапортом</p>
      <div class="mt-3 space-y-2">
        <button
          v-for="item in pendingItems"
          :key="item.shiftPlanItemId"
          type="button"
          :data-test="`pending-item-${item.shiftPlanItemId}`"
          class="w-full rounded-lg border border-amber-200 bg-white p-4 text-left shadow-sm hover:border-amber-400"
          @click="openReport(item.shiftPlanItemId)"
        >
          <div class="flex flex-wrap items-center justify-between gap-2">
            <span class="font-semibold text-gray-800">
              {{ formatDate(item.shiftDate) }} · смена {{ item.position }}
            </span>
            <span class="text-sm text-gray-600">Сменный рапорт</span>
          </div>
          <div class="mt-2 text-sm text-gray-600">
            {{ formatDateTime(item.startAtUtc) }} — {{ formatDateTime(item.endAtUtc) }}
          </div>
          <div v-if="item.shiftLeadName" class="mt-1 text-sm text-gray-500">
            Старший: {{ item.shiftLeadName }}
          </div>
          <div v-if="item.hasOrphanOutput" class="mt-2 text-sm font-medium text-amber-700">
            Выпуск без литейщика
          </div>
        </button>
      </div>
    </section>

    <el-empty
      v-if="!store.currentItem && !store.loading"
      data-test="nearest-empty"
      description="Назначенная смена не найдена"
    />
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'
import { TASK_STATUS_LABELS } from '@/constants/taskStatus'
import { useShiftStaffingRefresh } from '@/composables/useShiftStaffingRefresh'
import { getPending } from '@/api/shiftClosures'
import { apiErrorMessage } from '@/utils/apiError'

const store = useShiftStaffingStore()
const router = useRouter()
const pendingItems = ref([])
const pendingLoading = ref(false)
store.activateNearest()

const { refresh } = useShiftStaffingRefresh({
  getEndAtUtc: () => store.nearest?.endAtUtc,
  refresh: () => store.loadNearest()
})

onMounted(() => {
  refresh()
  loadPending()
})

async function loadPending() {
  pendingLoading.value = true
  try {
    const response = await getPending()
    pendingItems.value = response.data ?? []
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить смены, требующие закрытия'))
  } finally {
    pendingLoading.value = false
  }
}

function statusMeta(status) {
  return getEffectiveStatusMeta(status)
}

function taskStatusLabel(status) {
  return TASK_STATUS_LABELS[status] ?? status
}

function formatDate(value) {
  return value ? dayjs(value).format('DD.MM.YYYY') : '—'
}

function formatDateTime(value) {
  return value ? dayjs(value).format('DD.MM.YYYY HH:mm') : '—'
}

function openItem() {
  const itemId = store.currentItem?.shift?.itemId
  if (itemId) router.push(`/shift-lead/items/${itemId}`)
}

function openReport(itemId) {
  if (itemId) router.push(`/shift-lead/items/${itemId}/report`)
}
</script>
