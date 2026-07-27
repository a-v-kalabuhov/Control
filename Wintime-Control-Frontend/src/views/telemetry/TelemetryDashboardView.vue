<template>
  <div class="p-4 space-y-4">
    <!-- Фильтры -->
    <div class="bg-white rounded-lg shadow-sm border p-4 space-y-3">
      <div class="flex flex-wrap items-center gap-4">
        <div class="flex items-center gap-2">
          <span class="text-sm text-gray-600">Режим:</span>
          <el-select v-model="mode" size="small" style="width: 160px" @change="onModeChange">
            <el-option label="История" value="history" />
            <el-option label="Живые данные" value="live" />
          </el-select>
        </div>

        <template v-if="mode === 'history'">
          <el-date-picker
            v-model="historyFrom" type="datetime" size="small"
            placeholder="Начало" format="YYYY-MM-DD HH:mm:ss" value-format="YYYY-MM-DDTHH:mm:ss"
          />
          <el-date-picker
            v-model="historyTo" type="datetime" size="small"
            placeholder="Конец" format="YYYY-MM-DD HH:mm:ss" value-format="YYYY-MM-DDTHH:mm:ss"
          />
          <el-button size="small" type="primary" @click="loadHistory">Показать</el-button>
        </template>

        <template v-else>
          <div class="flex items-center gap-2">
            <span class="text-sm text-gray-600">Период:</span>
            <el-select v-model="livePeriodMin" size="small" style="width: 140px" @change="restartLive">
              <el-option v-for="p in LIVE_PERIODS" :key="p.min" :label="p.label" :value="p.min" />
            </el-select>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-sm text-gray-600">Обновление:</span>
            <el-select v-model="livePollSec" size="small" style="width: 120px" @change="restartLive">
              <el-option v-for="s in LIVE_POLL_OPTIONS" :key="s" :label="`${s} c`" :value="s" />
            </el-select>
          </div>
        </template>
      </div>

      <!-- Сигналы -->
      <div class="flex flex-wrap items-center gap-3">
        <span class="text-sm text-gray-600">Сигналы:</span>
        <el-checkbox-group v-model="selected" @change="onSelectionChange">
          <el-checkbox v-for="s in availableSignals" :key="s.parameterName" :value="s.parameterName">
            {{ s.parameterName }}
          </el-checkbox>
        </el-checkbox-group>
      </div>

      <div v-if="truncated" class="text-sm text-amber-600">
        Показаны не все точки — сузьте период.
      </div>
    </div>

    <!-- Столбик диаграмм -->
    <div v-if="orderedSignals.length" class="space-y-3">
      <div v-for="(sig, idx) in orderedSignals" :key="sig.parameterName" class="relative">
        <div class="absolute right-2 top-1.5 z-10 flex gap-1">
          <el-button size="small" text :disabled="idx === 0" @click="move(idx, -1)">▲</el-button>
          <el-button size="small" text :disabled="idx === orderedSignals.length - 1" @click="move(idx, 1)">▼</el-button>
        </div>
        <SignalChart
          :signal="sig"
          :cycles="cycles"
          :segments="segments"
          :window-start-ms="windowStartMs"
          :window-end-ms="windowEndMs"
        />
      </div>
    </div>
    <el-empty v-else description="Выберите сигнал для отображения" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import SignalChart from '@/components/telemetry/SignalChart.vue'
import { telemetryApi } from '@/api/telemetry'
import { mergeLivePoints } from '@/utils/telemetryChart'

const LIVE_PERIODS = [
  { min: 1,   label: '1 минута' },
  { min: 5,   label: '5 минут' },
  { min: 15,  label: '15 минут' },
  { min: 30,  label: '30 минут' },
  { min: 60,  label: '1 час' },
  { min: 120, label: '2 часа' },
  { min: 240, label: '4 часа' },
]
const LIVE_POLL_OPTIONS = [2, 5, 10] // секунды между запросами; выбор пользователя

const route = useRoute()
const immId = route.params.id

const mode = ref('live')
const livePeriodMin = ref(15)
const livePollSec = ref(10) // частота live-опроса, сек (дефолт 10)
const historyFrom = ref(null)
const historyTo = ref(null)

const availableSignals = ref([]) // [{ parameterName, name, type }]
const selected = ref([])
const order = ref([])            // порядок parameterName в столбце

const signalsData = ref([])      // [{ parameterName, type, points }]
const cycles = ref([])
const segments = ref([])
const truncated = ref(false)

const windowStartMs = ref(Date.now() - 15 * 60000)
const windowEndMs = ref(Date.now())

let pollTimer = null

const orderedSignals = computed(() => {
  const byName = new Map(signalsData.value.map(s => [s.parameterName, s]))
  return order.value.map(n => byName.get(n)).filter(Boolean)
})

function toIsoUtc(ms) {
  return new Date(ms).toISOString()
}

async function fetchWindow(fromMs, toMs, { append = false, pointsFromMs = null } = {}) {
  if (selected.value.length === 0) {
    signalsData.value = []
    cycles.value = []
    segments.value = []
    return
  }
  try {
    const { data } = await telemetryApi.getDashboard(immId, {
      from: toIsoUtc(fromMs),
      to: toIsoUtc(toMs),
      parameters: selected.value,
      pointsFrom: pointsFromMs ? toIsoUtc(pointsFromMs) : undefined,
    })
    truncated.value = data.truncated
    cycles.value = data.cycles
    segments.value = data.statusSegments

    if (append) {
      const byName = new Map(signalsData.value.map(s => [s.parameterName, s]))
      signalsData.value = data.signals.map(s => {
        const prev = byName.get(s.parameterName)
        const points = prev
          ? mergeLivePoints(prev.points, s.points, windowStartMs.value)
          : s.points
        return { ...s, points }
      })
    } else {
      signalsData.value = data.signals
    }
  } catch (e) {
    const status = e?.response?.status
    if (status === 400) ElMessage.warning(e.response.data ?? 'Некорректный период')
    else ElMessage.error('Не удалось загрузить телеметрию')
  }
}

// ── История ──
async function loadHistory() {
  if (!historyFrom.value || !historyTo.value) {
    ElMessage.warning('Укажите начало и конец периода')
    return
  }
  const fromMs = new Date(historyFrom.value + 'Z').getTime()
  const toMs = new Date(historyTo.value + 'Z').getTime()
  if (toMs - fromMs > 4 * 3600_000) {
    ElMessage.warning('Период не может превышать 4 часа')
    return
  }
  windowStartMs.value = fromMs
  windowEndMs.value = toMs
  await fetchWindow(fromMs, toMs)
}

// ── Live ──
async function liveTick(initial = false) {
  const now = Date.now()
  windowEndMs.value = now
  windowStartMs.value = now - livePeriodMin.value * 60000
  // Окно всегда полное (from..now) — для циклов/статуса; точки телеметрии на приросте берём
  // дельтой через pointsFrom (от последней полученной точки).
  const pointsFromMs = initial ? null : Math.max(lastLoadedMs(), windowStartMs.value)
  await fetchWindow(windowStartMs.value, now, { append: !initial, pointsFromMs })
}

function lastLoadedMs() {
  let maxT = windowStartMs.value
  for (const s of signalsData.value) {
    const p = s.points[s.points.length - 1]
    if (p) maxT = Math.max(maxT, new Date(p.t).getTime())
  }
  return maxT
}

function startLive() {
  stopLive()
  liveTick(true)
  pollTimer = setInterval(() => liveTick(false), livePollSec.value * 1000)
}

function stopLive() {
  if (pollTimer) { clearInterval(pollTimer); pollTimer = null }
}

function restartLive() {
  if (mode.value === 'live') startLive()
}

function onModeChange() {
  if (mode.value === 'live') startLive()
  else { stopLive(); signalsData.value = [] }
}

function onSelectionChange() {
  syncOrder()
  if (mode.value === 'live') startLive()
  else if (historyFrom.value && historyTo.value) loadHistory()
}

function syncOrder() {
  // сохранить прежний порядок, добавить новые в конец, убрать снятые
  order.value = [
    ...order.value.filter(n => selected.value.includes(n)),
    ...selected.value.filter(n => !order.value.includes(n)),
  ]
}

function move(idx, delta) {
  const next = idx + delta
  if (next < 0 || next >= order.value.length) return
  const arr = [...order.value]
  ;[arr[idx], arr[next]] = [arr[next], arr[idx]]
  order.value = arr
}

onMounted(async () => {
  try {
    const { data } = await telemetryApi.getSignals(immId)
    availableSignals.value = data
    if (data.length) {
      selected.value = [data[0].parameterName] // по умолчанию первый сигнал
      syncOrder()
    }
  } catch {
    ElMessage.error('Не удалось загрузить список сигналов')
  }
  startLive() // режим по умолчанию — живые данные
})

onUnmounted(stopLive)
</script>
