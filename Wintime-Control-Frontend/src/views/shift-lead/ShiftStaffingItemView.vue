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
          <p class="mt-1 text-sm text-gray-500">Старший: {{ store.currentItem.shiftLeadName }}</p>
        </div>
        <el-button
          data-test="refresh-item"
          :loading="store.refreshing"
          :disabled="store.loading || store.saving"
          @click="refreshItem"
        >
          Обновить
        </el-button>
      </div>

      <el-alert
        v-if="showFutureHint"
        class="mb-4"
        type="info"
        :closable="false"
        title="Работа учитывается с начала смены"
      />

      <div class="space-y-4">
        <section
          v-for="imm in store.currentItem.imms"
          :key="imm.immId"
          :data-test="`imm-${imm.immId}`"
          class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm"
        >
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h3 class="text-lg font-semibold text-gray-800">{{ imm.name }}</h3>
              <p v-if="imm.inventoryNumber" class="text-sm text-gray-500">{{ imm.inventoryNumber }}</p>
            </div>
            <span
              class="rounded-full px-3 py-1 text-sm font-medium"
              :class="[statusMeta(imm.effectiveStatus).bg, statusMeta(imm.effectiveStatus).text]"
            >
              {{ statusMeta(imm.effectiveStatus).label }}
            </span>
          </div>

          <div class="mt-4">
            <h4 class="mb-2 text-sm font-semibold text-gray-700">Задания</h4>
            <div v-if="imm.tasks.length" class="space-y-2">
              <button
                v-for="task in imm.tasks"
                :key="task.id"
                type="button"
                :data-test="`task-${task.id}`"
                class="w-full rounded border border-gray-200 bg-gray-50 px-3 py-2 text-left hover:border-primary-400"
                @click="openTask(task.id)"
              >
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <span class="font-medium text-gray-800">{{ task.productFullArticle || task.moldName }}</span>
                  <span class="text-sm text-gray-600">{{ taskStatusLabel(task.status) }}</span>
                </div>
                <div class="mt-1 text-sm text-gray-600">
                  {{ task.moldName }} · {{ task.actualQuantity || 0 }} / {{ task.planQuantity }} шт.
                </div>
              </button>
            </div>
            <p v-else class="text-sm text-gray-500">Задание отсутствует</p>
          </div>

          <div class="mt-4">
            <div class="mb-2 flex items-center justify-between gap-2">
              <h4 class="text-sm font-semibold text-gray-700">
                Литейщики: {{ imm.activeOperatorCount }}/2
              </h4>
              <el-button
                v-if="canAssign(imm)"
                type="primary"
                size="small"
                :data-test="`assign-${imm.immId}`"
                :disabled="controlsDisabled"
                @click="openAssign(imm)"
              >
                Назначить
              </el-button>
            </div>

            <div v-if="imm.assignments.length" class="space-y-2">
              <div
                v-for="assignment in imm.assignments"
                :key="assignment.id"
                :data-test="`assignment-${assignment.id}`"
                class="rounded border px-3 py-2 text-sm"
                :class="assignment.unassignedAt ? 'border-gray-200 bg-gray-50 text-gray-500' : 'border-primary-200 bg-primary-50'"
              >
                <div class="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <div class="font-medium text-gray-800">
                      {{ assignment.operatorName }}
                      <span v-if="assignment.operatorEmployeeId" class="text-xs text-gray-500">
                        ({{ assignment.operatorEmployeeId }})
                      </span>
                    </div>
                    <div class="mt-1">Назначен: {{ formatDateTime(assignment.assignedAt) }}</div>
                    <div v-if="assignment.unassignedAt" class="mt-1">
                      Снят: {{ formatDateTime(assignment.unassignedAt) }}
                    </div>
                    <div v-else class="mt-1 text-primary-700">Активен · место {{ assignment.slot }}</div>
                  </div>
                  <el-button
                    v-if="!assignment.unassignedAt && store.canEditOperators"
                    type="danger"
                    plain
                    size="small"
                    :data-test="`unassign-${assignment.id}`"
                    :disabled="controlsDisabled"
                    @click="unassign(assignment)"
                  >
                    Снять
                  </el-button>
                </div>
              </div>
            </div>
            <p v-else class="text-sm text-gray-500">Литейщики не назначены</p>

            <div
              v-if="assigningImmId === imm.immId && store.canEditOperators"
              class="mt-3 rounded border border-primary-200 bg-primary-50 p-3"
            >
              <el-select
                v-model="selectedOperatorId"
                data-test="operator-select"
                class="w-full"
                filterable
                placeholder="Выберите литейщика"
                :loading="operatorsLoading"
                :disabled="controlsDisabled || operatorsLoading"
              >
                <el-option
                  v-for="operator in candidatesFor(imm)"
                  :key="operator.id"
                  :value="operator.id"
                  :label="operatorLabel(operator)"
                />
              </el-select>
              <div class="mt-3 flex justify-end gap-2">
                <el-button size="small" :disabled="store.saving" @click="closeAssign">Отмена</el-button>
                <el-button
                  type="primary"
                  size="small"
                  data-test="confirm-assign"
                  :disabled="!selectedOperatorId || controlsDisabled"
                  :loading="store.saving"
                  @click="assign(imm)"
                >
                  Назначить
                </el-button>
              </div>
            </div>
          </div>
        </section>
      </div>
    </template>

    <el-empty v-else-if="!store.loading" description="Смена не найдена" />
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'
import { TASK_STATUS_LABELS } from '@/constants/taskStatus'
import { useShiftStaffingRefresh } from '@/composables/useShiftStaffingRefresh'

const route = useRoute()
const router = useRouter()
const store = useShiftStaffingStore()
const itemId = String(route.params.itemId)
const assigningImmId = ref(null)
const selectedOperatorId = ref(null)
const operatorsLoading = ref(false)

const controlsDisabled = computed(() => store.loading || store.saving)
const showFutureHint = computed(() => {
  const shift = store.currentItem?.shift
  if (!shift || Date.now() >= Date.parse(shift.startAtUtc)) return false
  return store.currentItem.imms.some(imm =>
    imm.assignments.some(assignment => !assignment.unassignedAt))
})

// Home уже загрузил согласованную пару summary/detail. При прямом открытии
// запускаем тот же таймер; независимого второго polling поверх Home нет.
const openedFromHome = store.nearest?.itemId === itemId &&
  store.currentItem?.shift?.itemId === itemId
let guardedRefresh = null
if (!openedFromHome) {
  const { refresh } = useShiftStaffingRefresh({
    getEndAtUtc: () => store.currentItem?.shift?.endAtUtc,
    refresh: () => store.loadItem(itemId)
  })
  guardedRefresh = refresh
  onMounted(refresh)
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

function canAssign(imm) {
  return store.canEditOperators && imm.activeOperatorCount < 2
}

function candidatesFor(imm) {
  const activeHere = new Set(imm.assignments
    .filter(assignment => !assignment.unassignedAt)
    .map(assignment => assignment.operatorId))
  return store.operators.filter(operator => !activeHere.has(operator.id))
}

function operatorLabel(operator) {
  return operator.employeeId ? `${operator.fullName} (${operator.employeeId})` : operator.fullName
}

async function openAssign(imm) {
  if (!canAssign(imm) || controlsDisabled.value) return
  assigningImmId.value = String(imm.immId)
  selectedOperatorId.value = null
  operatorsLoading.value = true
  const result = await store.loadOperators()
  operatorsLoading.value = false
  if (!result.success) closeAssign()
}

function closeAssign() {
  assigningImmId.value = null
  selectedOperatorId.value = null
}

async function assign(imm) {
  if (!selectedOperatorId.value || !canAssign(imm) || controlsDisabled.value) return
  const result = await store.assign(itemId, imm.immId, selectedOperatorId.value)
  if (result.success) closeAssign()
}

async function unassign(assignment) {
  if (assignment.unassignedAt || !store.canEditOperators || controlsDisabled.value) return
  await store.unassign(assignment.id)
}

async function refreshItem() {
  if (store.saving) return
  if (guardedRefresh) await guardedRefresh()
  else await store.loadItem(itemId)
}

function openTask(taskId) {
  router.push(`/shift-lead/items/${itemId}/tasks/${taskId}`)
}
</script>
