<template>
  <div class="p-4 space-y-4">
    <!-- Фильтры -->
    <div class="bg-white rounded-lg shadow-sm border p-4 space-y-3">
      <div class="flex flex-wrap items-center gap-4">
        <el-button v-if="showBack" size="small" @click="goBack">
          <el-icon class="mr-1"><ArrowLeft /></el-icon>
          Назад
        </el-button>

        <div class="flex items-center gap-2">
          <span class="text-sm text-gray-600">ТПА:</span>
          <el-select
            :model-value="immId"
            size="small" style="width: 220px"
            placeholder="Выберите ТПА"
            filterable clearable
            @update:model-value="onImmSelect"
          >
            <el-option v-for="imm in immOptions" :key="imm.id" :label="imm.name" :value="imm.id" />
          </el-select>
        </div>

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
      <div v-if="immId" class="flex flex-wrap items-center gap-3">
        <span class="text-sm text-gray-600">Сигналы:</span>
        <el-checkbox-group v-model="selected" @change="onSelectionChange">
          <el-checkbox v-for="s in availableSignals" :key="s.parameterName" :value="s.parameterName">
            {{ s.parameterName }}
          </el-checkbox>
        </el-checkbox-group>
      </div>

      <div v-if="immId && truncated" class="text-sm text-amber-600">
        Показаны не все точки — сузьте период.
      </div>
    </div>

    <el-empty v-if="!immId" description="Выберите ТПА" />

    <!-- Столбик диаграмм -->
    <div v-else-if="orderedSignals.length" class="space-y-3">
      <div v-for="(sig, idx) in orderedSignals" :key="sig.parameterName" class="flex items-start gap-2">
        <SignalChart
          class="min-w-0 flex-1"
          :signal="sig"
          :cycles="cycles"
          :segments="segments"
          :window-start-ms="windowStartMs"
          :window-end-ms="windowEndMs"
        />
        <div class="reorder-buttons flex w-8 shrink-0 flex-col items-center gap-1 pt-1.5">
          <el-button size="small" text :disabled="idx === 0" @click="move(idx, -1)">▲</el-button>
          <el-button size="small" text :disabled="idx === orderedSignals.length - 1" @click="move(idx, 1)">▼</el-button>
        </div>
      </div>
    </div>
    <el-empty v-else description="Выберите сигнал для отображения" />
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import SignalChart from '@/components/telemetry/SignalChart.vue'
import { telemetryApi } from '@/api/telemetry'
import { immApi } from '@/api/imm'
import { mergeLivePoints, enforceMinOneSignal } from '@/utils/telemetryChart'
import { monitoringQueryWithImm, showBackToCard, isImmId } from '@/utils/monitoringNav'
import { useAuthStore } from '@/stores/auth'
import { jwtSubject } from '@/utils/jwt'
import {
  viewPrefsKey, browserStorage, readViewPrefs, writeViewPrefs, resolveSignalOrder,
} from '@/utils/monitoringViewPrefs'

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
const router = useRouter()

// Источник истины по ТПА — query ?imm=<id> (спека 2026-10-14-monitoring-page-design).
const immId = computed(() => (isImmId(route.query.imm) ? route.query.imm : null))
const showBack = computed(() => showBackToCard(route.query))

const authStore = useAuthStore()

// Вид (датчики и их порядок) запоминается на пару (пользователь, шаблон ТПА).
// После F5 профиль ещё не загружен (App.onMounted позже страницы) — берём sub из токена.
function viewKey(id) {
  const userId = authStore.user?.id ?? jwtSubject(authStore.accessToken)
  const templateId = immOptions.value.find(i => i.id === id)?.templateId
  return viewPrefsKey(userId, templateId)
}

function saveView() {
  writeViewPrefs(browserStorage(), viewKey(immId.value), order.value)
}
const immOptions = ref([]) // [{ id, name, templateId }] — действующие ТПА (+ архивный из query, если пришёл по ссылке)

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
let lastNonEmptySelection = [] // последний непустой выбор сигналов — для отката при попытке снять последний

const orderedSignals = computed(() => {
  const byName = new Map(signalsData.value.map(s => [s.parameterName, s]))
  return order.value.map(n => byName.get(n)).filter(Boolean)
})

function toIsoUtc(ms) {
  return new Date(ms).toISOString()
}

function clearData() {
  signalsData.value = []
  cycles.value = []
  segments.value = []
  truncated.value = false
}

async function fetchWindow(fromMs, toMs, { append = false, pointsFromMs = null } = {}) {
  const reqImmId = immId.value
  if (!reqImmId || selected.value.length === 0) {
    clearData()
    return
  }
  try {
    const { data } = await telemetryApi.getDashboard(reqImmId, {
      from: toIsoUtc(fromMs),
      to: toIsoUtc(toMs),
      parameters: selected.value,
      pointsFrom: pointsFromMs ? toIsoUtc(pointsFromMs) : undefined,
    })
    if (reqImmId !== immId.value) return // ответ для прежнего ТПА — пользователь уже сменил выбор
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
    if (reqImmId !== immId.value) return
    truncated.value = false
    const status = e?.response?.status
    if (status === 400) ElMessage.warning(e.response.data ?? 'Некорректный период')
    else ElMessage.error('Не удалось загрузить телеметрию')
  }
}

// ── История ──
async function loadHistory() {
  if (!immId.value) {
    ElMessage.warning('Выберите ТПА')
    return
  }
  if (!historyFrom.value || !historyTo.value) {
    ElMessage.warning('Укажите начало и конец периода')
    return
  }
  const fromMs = new Date(historyFrom.value).getTime()
  const toMs = new Date(historyTo.value).getTime()
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
  if (unmounted || !immId.value) return
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
  else { stopLive(); clearData() }
}

function onSelectionChange(next) {
  // el-checkbox-group уже применил next к selected.value через v-model к моменту @change;
  // enforceMinOneSignal — чистая функция, откатывает на прежний непустой выбор при попытке снять последний.
  const resolved = enforceMinOneSignal(next, lastNonEmptySelection)
  selected.value = resolved
  if (!next || next.length === 0) return // ничего не меняем, кроме отката чекбокса
  lastNonEmptySelection = [...resolved]
  syncOrder()
  saveView()
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
  saveView()
}

// ── Выбор ТПА ──
function onImmSelect(id) {
  router.replace({ query: monitoringQueryWithImm(route.query, id) })
}

function goBack() {
  router.push({ name: 'Dashboard', query: { imm: immId.value } })
}

async function loadImmOptions() {
  try {
    const { data } = await immApi.getList({ isActive: true })
    immOptions.value = data
      .map(i => ({ id: i.id, name: i.name, templateId: i.templateId }))
      .sort((a, b) => a.name.localeCompare(b.name, 'ru'))
  } catch {
    ElMessage.error('Не удалось загрузить список ТПА')
  }
}

function notFound(id) {
  ElMessage.warning('ТПА не найден')
  if (immId.value === id) onImmSelect(null)
}

// ТПА из query может отсутствовать в списке действующих (архивный — по ссылке). Подгружаем его отдельно.
async function ensureImmOption(id) {
  if (immOptions.value.some(i => i.id === id)) return true
  try {
    const { data } = await immApi.getById(id)
    if (!data?.id) { // SPA-fallback может вернуть 200 с HTML
      notFound(id)
      return false
    }
    if (!immOptions.value.some(i => i.id === id)) immOptions.value.push({ id: data.id, name: data.name, templateId: data.templateId })
    return true
  } catch (e) {
    if (e?.response?.status === 404) notFound(id)
    else ElMessage.error('Не удалось загрузить ТПА')
    return false
  }
}

async function switchImm(id) {
  stopLive()
  clearData()
  availableSignals.value = []
  selected.value = []
  order.value = []
  lastNonEmptySelection = []
  if (!id) return

  if (!(await ensureImmOption(id)) || immId.value !== id || unmounted) return
  try {
    const { data } = await telemetryApi.getSignals(id)
    if (immId.value !== id || unmounted) return
    availableSignals.value = data
    // сохранённый вид для шаблона этого ТПА; без него — первый сигнал
    const initial = resolveSignalOrder(
      data.map(s => s.parameterName),
      readViewPrefs(browserStorage(), viewKey(id)),
    )
    selected.value = [...initial]
    order.value = [...initial]
    lastNonEmptySelection = [...initial]
  } catch {
    if (immId.value === id) ElMessage.error('Не удалось загрузить список сигналов')
    return
  }
  // режим, период, частота опроса и даты истории сохраняются при смене ТПА
  if (mode.value === 'live') startLive()
  else if (historyFrom.value && historyTo.value) loadHistory()
}

// В query есть imm, но это не id (мусор в URL) — предупреждаем и убираем его.
function dropInvalidImm() {
  if (route.query.imm !== undefined && !isImmId(route.query.imm)) {
    ElMessage.warning('ТПА не найден')
    router.replace({ query: monitoringQueryWithImm(route.query, null) })
  }
}

// Наблюдатели заводим синхронно в setup (привязаны к компоненту и снимаются при unmount);
// пока не загружен список ТПА (ready=false) они молчат, начальную обработку делает onMounted.
let ready = false
let unmounted = false
watch(() => route.query.imm, () => {
  if (ready && !unmounted) dropInvalidImm()
})
watch(immId, (id) => {
  if (ready && !unmounted) switchImm(id)
})

onMounted(async () => {
  await loadImmOptions()
  if (unmounted) return
  ready = true
  if (route.query.imm !== undefined && !isImmId(route.query.imm)) dropInvalidImm()
  else await switchImm(immId.value) // режим по умолчанию — живые данные
})

onUnmounted(() => { unmounted = true; stopLive() })
</script>

<style scoped>
/* Element Plus разносит соседние кнопки по горизонтали — в колонке это лишний сдвиг */
.reorder-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}
.reorder-buttons :deep(.el-button) {
  padding: 2px 4px;
}
</style>
