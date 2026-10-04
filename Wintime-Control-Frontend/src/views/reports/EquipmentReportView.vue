<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Производительность оборудования</h2>
        <p class="text-gray-600 mt-1">Эффективное состояние каждого ТПА по суткам</p>
      </div>
      <div class="flex gap-2">
        <el-button @click="goBack">Назад</el-button>
        <el-button type="success" @click="exportExcel" :loading="exporting" :disabled="!canLoad">
          <el-icon class="mr-1"><Download /></el-icon>
          Excel
        </el-button>
      </div>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true" class="report-filters">
        <el-form-item label="Период">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            range-separator="—"
            start-placeholder="Начало"
            end-placeholder="Окончание"
            value-format="YYYY-MM-DD"
            class="w-64"
          />
        </el-form-item>
        <el-form-item v-if="hasArchived" label="Архивные ТПА" data-test="archive-filter">
          <el-radio-group v-model="archiveMode">
            <el-radio-button value="exclude">Нет</el-radio-button>
            <el-radio-button value="include">Да</el-radio-button>
            <el-radio-button value="only">Только</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="ТПА">
          <el-select
            v-model="selectedImmIds"
            multiple
            collapse-tags
            collapse-tags-tooltip
            filterable
            placeholder="Выберите ТПА"
            class="w-72"
          >
            <el-option
              v-for="imm in immOptions"
              :key="imm.id"
              :label="imm.isActive ? imm.name : `${imm.name} (архив)`"
              :value="imm.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-tooltip :disabled="canLoad" content="Выберите ТПА" placement="top">
            <el-button type="primary" @click="loadReport" :disabled="!canLoad" data-test="load-report">Сформировать</el-button>
          </el-tooltip>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Отчёт сформирован с другими фильтрами -->
    <el-alert
      v-if="isStale"
      data-test="stale-hint"
      type="warning"
      show-icon
      :closable="false"
      class="mb-4"
      title="Фильтры изменены — нажмите «Сформировать», чтобы обновить отчёт"
    />

    <div :class="{ 'stale-report': isStale }">
    <!-- Сводные показатели -->
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4" data-test="kpi">
      <div class="card">
        <p class="text-sm text-gray-500">Всего ТПА</p>
        <p class="text-2xl font-bold text-gray-800">{{ reportsStore.totalImms }}</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Эффективность парка</p>
        <p class="text-2xl font-bold" :class="efficiencyClass(reportsStore.fleetEfficiency)">
          {{ formatEfficiency(reportsStore.fleetEfficiency) }}
        </p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Проблемные ТПА</p>
        <p class="text-lg font-bold text-yellow-600">&lt; 70 %: {{ reportsStore.problemBelow70 }}</p>
        <p class="text-lg font-bold text-red-600">&lt; 50 %: {{ reportsStore.problemBelow50 }}</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Всего циклов</p>
        <p class="text-2xl font-bold text-gray-800">{{ reportsStore.totalCycles }}</p>
      </div>
    </div>

    <!-- Диаграммы и сводная таблица — на вкладках, чтобы фильтры и KPI оставались на виду -->
    <el-tabs v-model="activeTab" data-test="report-tabs">
      <el-tab-pane label="Диаграммы" name="charts" />
      <el-tab-pane label="Сводная таблица" name="table" />
    </el-tabs>

    <div v-loading="loading" class="min-h-[120px]">
      <el-empty v-if="!loading && immData.length === 0" description="Нет данных за период" />

      <!-- Строки по ТПА. v-if, а не скрытие: ECharts в скрытом контейнере инициализируется с нулевой шириной -->
      <template v-else-if="activeTab === 'charts'">
        <EquipmentImmRow
          v-for="item in immData"
          :key="item.immId"
          :item="item"
          :data-imm-id="item.immId"
          class="scroll-mt-20"
          :class="{ 'imm-highlight': highlightedImmId === item.immId }"
        />
      </template>

      <!-- Итоговая таблица -->
      <el-card v-else-if="immData.length > 0" data-test="summary-table">
        <template #header>
          <div class="flex items-center justify-between">
            <span class="font-semibold">
              Итоги за период
              <span v-if="reportData" class="text-gray-500 font-normal ml-2">
                {{ dayjs(reportData.dateFrom).format('DD.MM.YYYY') }} — {{ dayjs(reportData.dateTo).format('DD.MM.YYYY') }}
              </span>
            </span>
            <span class="text-sm text-gray-500">Нажмите на строку, чтобы открыть диаграмму ТПА</span>
          </div>
        </template>
        <el-table
          :data="immData"
          stripe
          style="width: 100%"
          :summary-method="getSummaries"
          show-summary
          row-class-name="cursor-pointer"
          @row-click="openImmChart"
        >
          <el-table-column label="ТПА" min-width="150" fixed>
            <template #default="{ row }">
              {{ row.immName }}<span v-if="!row.isActive" class="text-gray-400"> (архив)</span>
            </template>
          </el-table-column>
          <el-table-column
            v-for="k in REPORT_STATUS_KEYS"
            :key="k"
            :label="`${REPORT_STATUS[k].label} (ч)`"
            min-width="110"
            align="right"
          >
            <template #default="{ row }">{{ toHours(row.seconds?.[k]) }}</template>
          </el-table-column>
          <el-table-column prop="totalCycles" label="Циклы" width="90" align="right" />
          <el-table-column label="Ср. цикл (с)" width="110" align="right">
            <template #default="{ row }">{{ row.avgCycleSeconds > 0 ? row.avgCycleSeconds.toFixed(1) : '—' }}</template>
          </el-table-column>
          <el-table-column label="Эффективность" width="130" align="right">
            <template #default="{ row }">
              <span :class="efficiencyClass(row.efficiency)">{{ formatEfficiency(row.efficiency) }}</span>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { useReportsStore } from '@/stores/reports'
import { immApi } from '@/api/imm'
import EquipmentImmRow from '@/components/reports/EquipmentImmRow.vue'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'
import { toHours, formatEfficiency, efficiencyClass, sumSeconds, selectedIdsParam } from '@/utils/equipmentReport'

const router = useRouter()
const reportsStore = useReportsStore()

const loading = ref(false)
const exporting = ref(false)
// Последние 7 суток включая сегодня
const dateRange = ref([dayjs().subtract(6, 'day').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')])

// Фильтры не запоминаются между открытиями отчёта.
const allImms = ref([])
const archiveMode = ref('exclude')
const selectedImmIds = ref([])

const hasArchived = computed(() => allImms.value.some(i => !i.isActive))

const immOptions = computed(() => {
  if (archiveMode.value === 'only') return allImms.value.filter(i => !i.isActive)
  if (archiveMode.value === 'include') return allImms.value
  return allImms.value.filter(i => i.isActive)
})

// Смена режима архива — выбор сбрасывается на «все ТПА режима».
watch(archiveMode, () => {
  selectedImmIds.value = immOptions.value.map(i => i.id)
})

const canLoad = computed(() => selectedImmIds.value.length > 0)

// Вкладка не сбрасывается при повторном формировании отчёта.
const activeTab = ref('charts')

// Клик по строке таблицы: перейти на диаграммы и прокрутить к ТПА, ненадолго подсветив его карточку.
const highlightedImmId = ref(null)
let highlightTimer = null

const openImmChart = async (row) => {
  activeTab.value = 'charts'
  await nextTick()
  document.querySelector(`[data-imm-id="${row.immId}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  highlightedImmId.value = row.immId
  clearTimeout(highlightTimer)
  highlightTimer = setTimeout(() => { highlightedImmId.value = null }, 2000)
}

onUnmounted(() => clearTimeout(highlightTimer))

const reportData = computed(() => reportsStore.equipmentReport)
const immData = computed(() => reportData.value?.immData ?? [])

const requestParams = () => ({
  dateFrom: dateRange.value[0],
  dateTo: dateRange.value[1],
  immIds: selectedIdsParam(selectedImmIds.value, immOptions.value),
  archive: archiveMode.value
})

// Фильтры, с которыми сформирован показанный отчёт: при расхождении отчёт помечается устаревшим.
const filtersKey = () => JSON.stringify({
  dateRange: dateRange.value,
  archive: archiveMode.value,
  immIds: [...selectedImmIds.value].sort()
})
const loadedFiltersKey = ref(null)
const isStale = computed(() => loadedFiltersKey.value !== null && loadedFiltersKey.value !== filtersKey())

onMounted(async () => {
  try {
    const { data } = await immApi.getList()
    allImms.value = [...data].sort((a, b) => a.name.localeCompare(b.name))
  } catch {
    ElMessage.error('Ошибка загрузки списка ТПА')
    return
  }
  selectedImmIds.value = immOptions.value.map(i => i.id)
  if (canLoad.value) await loadReport()
})

const loadReport = async () => {
  if (!canLoad.value) return
  loading.value = true
  const key = filtersKey()
  try {
    const { success } = await reportsStore.loadEquipmentReport(requestParams())
    if (success) loadedFiltersKey.value = key
  } finally {
    loading.value = false
  }
}

const exportExcel = async () => {
  exporting.value = true
  try {
    await reportsStore.exportToExcel('equipment', requestParams())
  } finally {
    exporting.value = false
  }
}

const goBack = () => {
  router.push('/reports')
}

const getSummaries = ({ data }) => {
  const totals = sumSeconds(data)
  return [
    'Итого:',
    ...REPORT_STATUS_KEYS.map(k => toHours(totals[k])),
    data.reduce((sum, row) => sum + row.totalCycles, 0),
    '—',
    formatEfficiency(reportsStore.fleetEfficiency)
  ]
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}

/* Отступ под полями = отступу над ними (padding карточки); при переносе строк — row-gap */
.report-filters {
  @apply flex flex-wrap gap-y-3;
}
.report-filters :deep(.el-form-item) {
  @apply mb-0;
}

/* Карточка ТПА, к которой перешли из сводной таблицы */
.imm-highlight {
  box-shadow: 0 0 0 2px var(--el-color-primary);
  transition: box-shadow 0.3s;
}

/* Показанный отчёт не соответствует текущим фильтрам */
.stale-report {
  opacity: 0.5;
  transition: opacity 0.2s;
}
</style>
