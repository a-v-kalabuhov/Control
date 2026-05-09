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
            <div class="grid grid-cols-4 gap-3">
              <div class="bg-green-50 border border-green-100 rounded-lg p-3 text-center">
                <div class="text-xl font-bold text-green-600">{{ summary.auto }}</div>
                <div class="text-xs text-gray-500 mt-0.5">Авто</div>
              </div>
              <div class="bg-yellow-50 border border-yellow-100 rounded-lg p-3 text-center">
                <div class="text-xl font-bold text-yellow-600">{{ summary.manual }}</div>
                <div class="text-xs text-gray-500 mt-0.5">Наладка</div>
              </div>
              <div class="bg-red-50 border border-red-100 rounded-lg p-3 text-center">
                <div class="text-xl font-bold text-red-600">{{ summary.alarm }}</div>
                <div class="text-xs text-gray-500 mt-0.5">Аварии</div>
              </div>
              <div class="bg-gray-50 border border-gray-200 rounded-lg p-3 text-center">
                <div class="text-xl font-bold text-gray-500">{{ summary.offline }}</div>
                <div class="text-xs text-gray-500 mt-0.5">Оффлайн</div>
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
        />
        <div class="mt-2 text-right text-xs text-gray-400">Наведите курсор на элемент для подробностей</div>
      </div>

    </div>

    <template #footer>
      <el-button @click="visible = false">Закрыть</el-button>
      <el-button type="primary" :loading="loading" @click="loadData">
        <el-icon class="mr-1"><Refresh /></el-icon>
        Обновить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, shallowRef, computed } from 'vue'
import { dashboardApi } from '@/api/dashboard'
import { tasksApi } from '@/api/tasks'
import { ElMessage } from 'element-plus'
import { useDashboardStore } from '@/stores/dashboard'
import { getCurrentShift } from '@/constants/shift'
import ImmCard from '@/components/dashboard/ImmCard.vue'
import ShiftTimeline from '@/components/dashboard/ShiftTimeline.vue'

const props = defineProps({
  immId:      { type: String, required: true },
  modelValue: { type: Boolean, default: false },
})
const emit = defineEmits(['update:modelValue'])

const visible = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})

const dashboardStore = useDashboardStore()

const loading        = ref(false)
const statusSegments = ref([])
const tasks          = ref([])
// shallowRef: содержимое объекта не оборачивается в Proxy,
// иначе Date-арифметика (clampE - clampS) ломается и даёт NaN
const shift          = shallowRef(getCurrentShift())

const immData = computed(() => dashboardStore.imms.find(i => i.id === props.immId) ?? null)

const dialogTitle = computed(() => immData.value ? `ТПА: ${immData.value.name} — смена` : 'Детали смены')

const shiftLabel = computed(() => {
  const fmtTime = (d) => d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
  const fmtDate = (d) => d.toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric' })
  return `${fmtTime(shift.value.start)} — ${fmtTime(shift.value.end)}, ${fmtDate(shift.value.start)}`
})

// --- Сводка по статусам, вычисленная из сегментов ---
const STATUS_LEGEND = [
  { status: 'Auto',    label: 'Авто',     color: '#22c55e' },
  { status: 'Manual',  label: 'Наладка',  color: '#eab308' },
  { status: 'Alarm',   label: 'Авария',   color: '#ef4444' },
  { status: 'Offline', label: 'Оффлайн',  color: '#9ca3af' },
]

function segDurationMs(seg) {
  const startMs      = new Date(seg.ChangedAt).getTime()
  const endMs        = seg.EndedAt ? new Date(seg.EndedAt).getTime() : Date.now()
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
  const totals = { auto: 0, manual: 0, alarm: 0, offline: 0 }
  for (const seg of statusSegments.value) {
    const ms = segDurationMs(seg)
    if (seg.Status === 'Auto')             totals.auto   += ms
    else if (seg.Status === 'Manual')      totals.manual += ms
    else if (seg.Status === 'Alarm')       totals.alarm  += ms
    else /* Offline / Idle */              totals.offline += ms
  }
  return {
    auto:    msToHm(totals.auto),
    manual:  msToHm(totals.manual),
    alarm:   msToHm(totals.alarm),
    offline: msToHm(totals.offline),
  }
})

// --- Загрузка данных ---
const loadData = async () => {
  if (!props.immId) return
  loading.value = true
  shift.value = getCurrentShift()

  const fromIso = shift.value.start.toISOString()
  const toIso   = shift.value.end.toISOString()

  try {
    const [historyRes, tasksRes] = await Promise.all([
      dashboardApi.getImmStatusHistory(props.immId, { from: fromIso, to: toIso }),
      tasksApi.getList({ immId: props.immId, dateFrom: fromIso, dateTo: toIso }),
    ])
    statusSegments.value = historyRes.data
    tasks.value          = tasksRes.data?.items ?? tasksRes.data ?? []
  } catch (error) {
    ElMessage.error('Ошибка загрузки данных смены')
    console.error(error)
  } finally {
    loading.value = false
  }
}

const onOpen   = () => loadData()
const onClosed = () => {
  statusSegments.value = []
  tasks.value          = []
}
</script>
