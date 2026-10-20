<template>
  <div v-loading="loading" class="min-h-[120px]">
    <div v-if="loadFailed && !results" data-test="load-error" class="text-gray-500">
      <p class="mb-2">Не удалось загрузить итоги</p>
      <el-button size="small" @click="load">Повторить</el-button>
    </div>

    <p v-else-if="results && !results.releaseStarted" class="text-gray-500">Выпуск ещё не начат</p>

    <template v-else-if="results">
      <p class="text-sm text-gray-600 mb-4">
        Окно выпуска: {{ formatDate(results.releaseFrom) }} —
        {{ results.inProgress ? 'сейчас' : formatDate(results.releaseTo) }}
        ({{ formatDuration(results.time.windowSeconds) }})
        <el-tag v-if="results.inProgress" size="small" type="warning" class="ml-2">в работе</el-tag>
      </p>

      <h4 class="font-semibold text-gray-700 mb-2">Выпуск</h4>
      <div data-test="output" class="mb-6">
        <div class="grid grid-cols-5 gap-2 text-center">
          <div><div class="text-xs text-gray-500">План</div><div class="tabular-nums">{{ results.output.planQuantity }}</div></div>
          <div><div class="text-xs text-gray-500">Выпущено</div><div class="tabular-nums">{{ results.output.actualQuantity }}</div></div>
          <div><div class="text-xs text-gray-500">Брак</div><div class="tabular-nums">{{ results.output.defectQuantity }}</div></div>
          <div><div class="text-xs text-gray-500">Годных</div><div class="tabular-nums">{{ results.output.goodQuantity }}</div></div>
          <div>
            <div class="text-xs text-gray-500">Перевыпуск</div>
            <div data-test="overrun" class="tabular-nums">{{ results.output.overrunQuantity > 0 ? results.output.overrunQuantity : '—' }}</div>
          </div>
        </div>
        <p v-if="results.output.deviationReason" data-test="deviation-reason" class="text-sm mt-3">
          <span class="text-gray-500">Причина отклонения от плана:</span> {{ results.output.deviationReason }}
        </p>
      </div>

      <h4 data-test="cycles-header" class="font-semibold text-gray-700 mb-2">
        Циклы ({{ results.cycles.count }} закрытых)
      </h4>
      <table class="w-full text-sm mb-6">
        <thead>
          <tr class="text-gray-500 text-xs">
            <th class="text-left font-normal"></th>
            <th class="text-right font-normal">Факт</th>
            <th class="text-right font-normal">Эталон</th>
            <th class="text-right font-normal">Откл.</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in cycleRows" :key="row.key" :data-test="`cycle-${row.key}`">
            <td>{{ row.label }}</td>
            <td class="text-right tabular-nums">{{ formatSeconds(row.fact) }}</td>
            <td class="text-right tabular-nums">{{ row.plan != null ? `${row.plan} с` : '—' }}</td>
            <td class="text-right tabular-nums">
              <span data-test="deviation" :class="deviationClass(row.deviation)">{{ formatDeviation(row.deviation) }}</span>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-if="results.cycles.avgCushion != null" data-test="cushion" class="text-sm -mt-4 mb-6 flex justify-between">
        <span>Подушка</span>
        <span class="tabular-nums">
          ср. {{ formatCushion(results.cycles.avgCushion) }}
          <span class="text-gray-500">(мин {{ formatCushion(results.cycles.minCushion) }} · макс {{ formatCushion(results.cycles.maxCushion) }})</span>
        </span>
      </div>

      <h4 class="font-semibold text-gray-700 mb-2">Время выпуска</h4>
      <div class="text-sm">
        <div v-for="row in timeRows" :key="row.key" :data-test="`time-${row.key}`" class="flex justify-between py-0.5">
          <span class="flex items-center gap-2">
            <span class="inline-block w-3 h-3 rounded-sm" :style="{ backgroundColor: row.color }" />
            {{ row.label }}
          </span>
          <span class="tabular-nums">
            {{ formatDuration(row.seconds) }}
            <span class="inline-block w-12 text-right text-gray-500">{{ row.share ?? '—' }} %</span>
          </span>
        </div>
        <!-- Полоса долей (не хронология — хронология в карточке ТПА) -->
        <div class="flex h-3 rounded overflow-hidden mt-2 bg-gray-100">
          <div
            v-for="row in timeRows"
            :key="row.key"
            :style="{ width: `${row.share ?? 0}%`, backgroundColor: row.color }"
          />
        </div>
      </div>
    </template>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import dayjs from 'dayjs'
import { tasksApi } from '@/api/tasks'
import { EFFECTIVE_STATUS } from '@/constants/effectiveStatus'
import { formatDuration } from '@/utils/taskPlan'
import { formatSeconds, sharePercent, deviationPercent, formatDeviation, formatCushion } from '@/utils/taskResults'

const props = defineProps({
  taskId: { type: String, required: true }
})

const REFRESH_MS = 60_000

const results = ref(null)
const loading = ref(false)
const loadFailed = ref(false)
let refreshTimer = null
let unmounted = false

const cycleRows = computed(() => {
  const c = results.value?.cycles
  if (!c) return []
  return [
    { key: 'injection', label: 'Цикл литья', fact: c.avgInjectionSeconds, plan: c.plannedInjectionSeconds },
    { key: 'full', label: 'Полный цикл', fact: c.avgFullCycleSeconds, plan: c.plannedFullCycleSeconds }
  ].map(r => ({ ...r, deviation: deviationPercent(r.fact, r.plan) }))
})

const timeRows = computed(() => {
  const t = results.value?.time
  if (!t) return []
  return [
    { key: 'Production', label: EFFECTIVE_STATUS.Production.label, seconds: t.productionSeconds },
    { key: 'Downtime', label: `Простои (${t.downtimeCount})`, seconds: t.downtimeSeconds },
    { key: 'Offline', label: EFFECTIVE_STATUS.Offline.label, seconds: t.offlineSeconds }
  ].map(r => ({ ...r, color: EFFECTIVE_STATUS[r.key].hex, share: sharePercent(r.seconds, t.windowSeconds) }))
})

function deviationClass(pct) {
  if (pct === null || pct === 0) return ''
  return pct < 0 ? 'text-green-600' : 'text-red-600'
}

function formatDate(date) {
  return date ? dayjs(date).format('DD.MM.YYYY HH:mm') : '—'
}

async function load() {
  loading.value = results.value === null   // спиннер только на первой загрузке, не на тихом обновлении
  try {
    const res = await tasksApi.getResults(props.taskId)
    if (unmounted) return
    results.value = res.data
    loadFailed.value = false
  } catch (error) {
    console.error(error)
    loadFailed.value = true
  } finally {
    loading.value = false
  }
  clearTimeout(refreshTimer)
  if (!unmounted && results.value?.inProgress) refreshTimer = setTimeout(load, REFRESH_MS)
}

onMounted(load)
onUnmounted(() => { unmounted = true; clearTimeout(refreshTimer) })
</script>
