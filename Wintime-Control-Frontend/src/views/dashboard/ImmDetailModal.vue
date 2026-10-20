<template>
  <el-dialog
    v-model="visible"
    width="950px"
    :close-on-click-modal="false"
    :title="dialogTitle"
    @open="onOpen"
    @closed="onClosed"
  >
    <div v-loading="loading" class="min-h-40">

      <!-- ====== Верхняя строка: карточка + сводка ====== -->
      <div class="flex gap-5 mb-5 items-start">

        <!-- Карточка ТПА (read-only) -->
        <div class="flex-shrink-0 pointer-events-none">
          <ImmCard v-if="immData" :imm="immData" />
          <div v-else class="w-56 h-40 bg-gray-100 rounded-lg animate-pulse"></div>
        </div>

        <!-- Правая часть: легенда + сводка по статусам -->
        <div class="flex-1 flex flex-col gap-4">

          <!-- Легенда -->
          <div>
            <div class="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">Статусы ТПА</div>
            <div class="flex flex-wrap gap-4">
              <span v-for="item in STATUS_LEGEND" :key="item.status" class="flex items-center gap-1.5 text-sm text-gray-600">
                <span class="inline-block w-5 h-3 rounded-sm" :style="{ background: item.color }"></span>
                {{ item.label }}
              </span>
            </div>
          </div>

          <!-- Сводка статусов за смену -->
          <div>
            <div class="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
              Итоги смены ({{ shiftLabel }})
            </div>
            <div class="grid grid-cols-6 gap-3">
              <div v-for="item in STATUS_LEGEND" :key="item.status"
                   class="border rounded-lg p-3 text-center" :style="{ borderColor: item.color }">
                <div class="text-xl font-bold" :style="{ color: item.color }">{{ summary[item.status] }}</div>
                <div class="text-xs text-gray-500 mt-0.5">{{ item.label }}</div>
              </div>
            </div>
          </div>

        </div>
      </div>

      <!-- Разделитель -->
      <div class="border-t border-gray-100 mb-4"></div>

      <!-- ====== Хронология ====== -->
      <div>
        <div class="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-3">Хронология смены</div>
        <ShiftTimeline
          :status-segments="statusSegments"
          :tasks="tasks"
          :shift-start="shift.start"
          :shift-end="shift.end"
          @task-click="openTask"
        />
        <div class="mt-2 text-right text-xs text-gray-400">
          Наведите курсор на элемент для подробностей, нажмите на задание — откроется его карточка
        </div>
      </div>

    </div>

    <template #footer>
      <el-button v-if="canAccess(['Admin', 'Manager'])" type="primary" plain @click="goToTelemetry">Телеметрия</el-button>
      <el-button @click="visible = false">Закрыть</el-button>
      <el-button type="primary" :loading="loading" @click="loadData">
        <el-icon class="mr-1"><Refresh /></el-icon>
        Обновить
      </el-button>
    </template>

    <!-- Карточка задания из хронологии — только просмотр (append-to-body: поверх этого окна) -->
    <TaskDetailModal v-model="taskCardVisible" :task="selectedTask" readonly append-to-body />
  </el-dialog>
</template>

<script setup>
import { ref, shallowRef, computed, watch, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { dashboardApi } from '@/api/dashboard'
import { tasksApi } from '@/api/tasks'
import { shiftsApi } from '@/api/shifts'
import { ElMessage } from 'element-plus'
import { useDashboardStore } from '@/stores/dashboard'
import { usePermissions } from '@/composables/usePermissions'
import { computeCurrentShift } from '@/constants/shift'
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS } from '@/constants/effectiveStatus'
import { mergeTimelineTasks, plannedRangeForShift } from '@/utils/shiftTimeline'
import ImmCard from '@/components/dashboard/ImmCard.vue'
import ShiftTimeline from '@/components/dashboard/ShiftTimeline.vue'
import TaskDetailModal from '@/views/tasks/TaskDetailModal.vue'

const props = defineProps({
  immId:      { type: String, required: true },
  modelValue: { type: Boolean, default: false },
})
const emit = defineEmits(['update:modelValue'])

const router = useRouter()
// Телеметрия доступна только Admin/Manager (API — ManagerOrAdmin), Observer кнопку не видит.
const { canAccess } = usePermissions()

const visible = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})

function goToTelemetry() {
  // Модалку не закрываем: она уходит вместе с дашбордом; закрытие сбросило бы ?imm на дашборде
  // параллельно с переходом.
  router.push({ name: 'Monitoring', query: { imm: props.immId, from: 'card' } })
}

const dashboardStore = useDashboardStore()

const loading        = ref(false)
const statusSegments = ref([])
const tasks          = ref([])
const selectedTask   = ref(null)
const taskCardVisible = ref(false)

function openTask(task) {
  selectedTask.value = task
  taskCardVisible.value = true
}
const shiftSchedule  = ref([])
// shallowRef: содержимое объекта не оборачивается в Proxy,
// иначе Date-арифметика (clampE - clampS) ломается и даёт NaN
const shift          = shallowRef(computeCurrentShift([]))

const immData = computed(() => dashboardStore.imms.find(i => i.id === props.immId) ?? null)

const dialogTitle = computed(() => immData.value ? `ТПА: ${immData.value.name} — смена` : 'Детали смены')

const shiftLabel = computed(() => {
  const fmtTime = (d) => d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
  const fmtDate = (d) => d.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' })
  return `${fmtTime(shift.value.start)} — ${fmtTime(shift.value.end)}, ${fmtDate(shift.value.start)}`
})

// --- Сводка по статусам, вычисленная из сегментов ---
const STATUS_LEGEND = EFFECTIVE_STATUS_KEYS.map(k => ({
  status: k, label: EFFECTIVE_STATUS[k].label, color: EFFECTIVE_STATUS[k].hex,
}))

function segDurationMs(seg) {
  const startMs      = new Date(seg.changedAt).getTime()
  const endMs        = seg.endedAt ? new Date(seg.endedAt).getTime() : Date.now()
  const shiftStartMs = shift.value.start.getTime()
  const shiftEndMs   = shift.value.end.getTime()
  const clampS = Math.max(startMs, shiftStartMs)
  const clampE = Math.min(endMs, shiftEndMs)
  return Math.max(0, clampE - clampS)
}

function msToHm(ms) {
  if (!isFinite(ms) || ms < 0) return '—'
  const totalMin = Math.round(ms / 60_000)
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h > 0 && m > 0) return `${h}ч ${m}м`
  if (h > 0) return `${h}ч`
  return `${m}м`
}

const summary = computed(() => {
  const totals = { Production: 0, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 0, Offline: 0 }
  for (const seg of statusSegments.value) {
    const ms = segDurationMs(seg)
    const key = seg.effectiveStatus
    if (key in totals) totals[key] += ms
  }
  const out = {}
  for (const k of Object.keys(totals)) out[k] = msToHm(totals[k])
  return out
})

// --- Загрузка данных ---
const items = (res) => res.data?.items ?? res.data ?? []

const loadData = async () => {
  if (!props.immId) return
  loading.value = true
  shift.value = computeCurrentShift(shiftSchedule.value)

  const fromIso = shift.value.start.toISOString()
  const toIso   = shift.value.end.toISOString()

  // BL-40: кроме активных в смене — запланированные около смены (для ряда «План»),
  // включая ещё не начатые. Точный отбор по plannedStartAt — в ShiftTimeline.
  const { plannedFrom, plannedTo } = plannedRangeForShift(shift.value.start, shift.value.end)

  try {
    // Запросы заданий не фатальны: строка статусов должна показываться, даже если список
    // заданий не загрузился, — упавший запрос даёт пустой список, без тоста.
    const [historyRes, taskRes] = await Promise.all([
      dashboardApi.getImmEffectiveStatusHistory(props.immId, { from: fromIso, to: toIso }),
      Promise.allSettled([
        tasksApi.getList({ immId: props.immId, activeFrom: fromIso, activeTo: toIso }),
        tasksApi.getList({ immId: props.immId, plannedFrom, plannedTo }),
      ]),
    ])
    const [active, planned] = taskRes.map(r => (r.status === 'fulfilled' ? items(r.value) : []))
    statusSegments.value = historyRes.data
    tasks.value          = mergeTimelineTasks(active, planned)
  } catch (error) {
    ElMessage.error('Ошибка загрузки данных смены')
    console.error(error)
  } finally {
    loading.value = false
  }
}

let refreshTimer = null
let unmounted = false

const onOpen = async () => {
  try {
    const res = await shiftsApi.getShifts()
    shiftSchedule.value = res.data ?? []
  } catch {
    shiftSchedule.value = []
  }
  if (unmounted || !visible.value) return // компонент снят/диалог закрыт, пока шёл await — таймер не заводим
  loadData()
  clearInterval(refreshTimer)
  refreshTimer = setInterval(loadData, 60_000)
}

// immId сменился при открытой карточке (история браузера) — перезагружаем данные
watch(() => props.immId, () => { if (visible.value) loadData() })

const onClosed = () => {
  clearInterval(refreshTimer)
  refreshTimer = null
  statusSegments.value = []
  tasks.value          = []
}

onUnmounted(() => { unmounted = true; clearInterval(refreshTimer) })
</script>
