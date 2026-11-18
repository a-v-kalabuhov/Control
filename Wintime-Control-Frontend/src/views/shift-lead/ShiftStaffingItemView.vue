<template>
  <div v-loading="store.loading" class="min-h-[160px]">
    <template v-if="currentItem">
      <div class="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 class="text-xl font-bold text-gray-800">
            Смена {{ currentItem.shift.position }} · {{ formatDate(currentItem.shift.shiftDate) }}
          </h2>
          <p class="mt-1 text-sm text-gray-600">
            {{ formatDateTime(currentItem.shift.startAtUtc) }} —
            {{ formatDateTime(currentItem.shift.endAtUtc) }}
          </p>
          <p class="mt-1 text-sm text-gray-500">Старший: {{ currentItem.shiftLeadName }}</p>
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
          v-for="imm in currentItem.imms"
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

          <ShiftClosureTimeline
            v-if="timelineFor(imm.immId)"
            class="mt-4"
            :timeline="timelineFor(imm.immId)"
            :can-edit="canEditOperators"
            :disabled="controlsDisabled"
            @assign="openAssign(imm, $event)"
          />

          <div class="mt-4">
            <div class="mb-2 flex items-center justify-between gap-2">
              <h4 class="text-sm font-semibold text-gray-700">
                Литейщики: {{ imm.activeOperatorCount }}/2
              </h4>
              <el-button
                v-if="canEditOperators"
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
                :class="isActiveAssignment(assignment) ? 'border-primary-200 bg-primary-50' : 'border-gray-200 bg-gray-50 text-gray-500'"
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
                    <div
                      v-else
                      class="mt-1"
                      :class="isActiveAssignment(assignment) ? 'text-primary-700' : 'text-gray-500'"
                    >
                      {{ isActiveAssignment(assignment) ? 'Активен' : 'До конца смены' }}
                    </div>
                  </div>
                  <div v-if="canEditOperators" class="flex gap-2">
                    <el-button
                      size="small"
                      :data-test="`edit-${assignment.id}`"
                      :disabled="controlsDisabled"
                      @click="openEdit(assignment)"
                    >
                      Изменить
                    </el-button>
                    <el-button
                      v-if="!assignment.unassignedAt"
                      type="danger"
                      plain
                      size="small"
                      :data-test="`unassign-${assignment.id}`"
                      :disabled="controlsDisabled"
                      @click="openUnassign(assignment)"
                    >
                      Снять
                    </el-button>
                  </div>
                </div>
              </div>
            </div>
            <p v-else class="text-sm text-gray-500">Литейщики не назначены</p>
          </div>
        </section>
      </div>
    </template>

    <el-empty v-else-if="!store.loading" description="Смена не найдена" />

    <OperatorIntervalDialog
      v-model="dialogVisible"
      :mode="dialogMode"
      :item-id="itemId"
      :imm-id="dialogImmId"
      :assignment="dialogAssignment"
      :initial-interval="dialogInitialInterval"
      :operators="store.operators"
      :operators-loading="operatorsLoading"
      :can-edit="canEditOperators"
      @reload="reloadClosureReport"
    />
  </div>
</template>

<script setup>
import { computed, effectScope, onMounted, onUnmounted, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'
import { getEffectiveStatusMeta } from '@/constants/effectiveStatus'
import { TASK_STATUS_LABELS } from '@/constants/taskStatus'
import { useShiftStaffingRefresh } from '@/composables/useShiftStaffingRefresh'
import { useShiftClosureEditor } from '@/composables/useShiftClosureEditor'
import ShiftClosureTimeline from '@/components/shift-closures/ShiftClosureTimeline.vue'
import OperatorIntervalDialog from '@/components/shift-staffing/OperatorIntervalDialog.vue'

const route = useRoute()
const router = useRouter()
const store = useShiftStaffingStore()
const itemId = computed(() => String(route.params.itemId))
const currentItem = computed(() => {
  const item = store.currentItem
  return item?.shift?.itemId === itemId.value ? item : null
})
const canEditOperators = computed(() => currentItem.value?.shift?.canEditOperators === true)
const isHistorical = computed(() => currentItem.value?.shift?.state === 'Historical')
const closureEditor = shallowRef(useShiftClosureEditor(itemId.value))
const closureReport = computed(() => closureEditor.value.report.value)
const dialogVisible = ref(false)
const dialogMode = ref('create')
const dialogImmId = ref(null)
const dialogAssignment = ref(null)
const dialogInitialInterval = ref(null)
const operatorsLoading = ref(false)
let operatorsSeq = 0
let pollingScope = null
let guardedRefresh = null
let mounted = false

const controlsDisabled = computed(() => store.loading || store.saving)
const showFutureHint = computed(() => {
  const shift = currentItem.value?.shift
  if (!shift || Date.now() >= Date.parse(shift.startAtUtc)) return false
  return currentItem.value.imms.some(imm =>
    imm.assignments.some(assignment => !assignment.unassignedAt))
})

// Вход в Item синхронно отменяет nearest/mutations прежнего экрана и скрывает
// чужой cached item ещё до первого render нового маршрута.
store.activateItem(itemId.value)

function startPolling(id) {
  pollingScope?.stop()
  pollingScope = effectScope()
  pollingScope.run(() => {
    const { refresh } = useShiftStaffingRefresh({
      getEndAtUtc: () => currentItem.value?.shift?.endAtUtc,
      refresh: () => store.loadItem(id)
    })
    guardedRefresh = refresh
    refresh()
  })
}

function loadClosureReport(options) {
  return closureEditor.value.load(options)
}

function reloadClosureReport() {
  return loadClosureReport({ preserveDirty: true })
}

onMounted(() => {
  mounted = true
  startPolling(itemId.value)
  loadClosureReport()
})

onUnmounted(() => {
  mounted = false
  pollingScope?.stop()
  pollingScope = null
})

watch(itemId, (id, previousId) => {
  if (id === previousId) return
  closeDialog()
  operatorsLoading.value = false
  store.activateItem(id)
  closureEditor.value = useShiftClosureEditor(id)
  if (mounted) {
    startPolling(id)
    loadClosureReport()
  }
})

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

function isActiveAssignment(assignment) {
  return !assignment.unassignedAt && !isHistorical.value
}

function timelineFor(immId) {
  return closureReport.value?.timelines?.find(timeline => timeline.immId === immId) ?? null
}

function openDialog(mode, immId, assignment = null, initialInterval = null) {
  dialogMode.value = mode
  dialogImmId.value = immId
  dialogAssignment.value = assignment
  dialogInitialInterval.value = initialInterval
  dialogVisible.value = true
}

async function openAssign(imm, initialInterval = null) {
  if (!canEditOperators.value || controlsDisabled.value) return
  const seq = ++operatorsSeq
  openDialog('create', imm.immId, null, initialInterval)
  operatorsLoading.value = true
  const result = await store.loadOperators()
  if (seq !== operatorsSeq) return
  operatorsLoading.value = false
  if (!result.success) closeDialog()
}

function openEdit(assignment) {
  if (!canEditOperators.value || controlsDisabled.value) return
  operatorsSeq += 1
  operatorsLoading.value = false
  openDialog('edit', assignment.immId, assignment)
}

function openUnassign(assignment) {
  if (assignment.unassignedAt || !canEditOperators.value || controlsDisabled.value) return
  operatorsSeq += 1
  operatorsLoading.value = false
  openDialog('unassign', assignment.immId, assignment)
}

function closeDialog() {
  operatorsSeq += 1
  dialogVisible.value = false
  dialogAssignment.value = null
  dialogInitialInterval.value = null
}

async function refreshItem() {
  if (store.saving) return
  if (guardedRefresh) await guardedRefresh()
  else await store.loadItem(itemId.value)
}

function openTask(taskId) {
  router.push(`/shift-lead/items/${itemId.value}/tasks/${taskId}`)
}
</script>
