<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Активы цеха</h2>
        <p class="text-gray-600 mt-1">Учёт пресс-форм и работы наладчиков</p>
      </div>
      <div class="flex gap-2">
        <el-button @click="goBack">Назад</el-button>
        <el-button type="success" @click="exportExcel" :loading="exporting">
          <el-icon class="mr-1"><Download /></el-icon>
          Excel
        </el-button>
      </div>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true">
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
        <el-form-item label="Тип">
          <el-select v-model="reportType" class="w-48">
            <el-option label="Пресс-формы" value="Molds" />
            <el-option label="Наладчики" value="Personnel" />
            <el-option label="Пресс-формы по ТПА" value="MoldsByImm" />
            <el-option label="Наладчики по ТПА" value="PersonnelByImm" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadReport">Сформировать</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Таблица: Пресс-формы -->
    <el-card v-loading="loading" v-if="reportType === 'Molds'">
      <el-table :data="reportData?.moldData || []" stripe style="width: 100%">
        <el-table-column prop="moldName" label="Пресс-форма" min-width="200" />
        <el-table-column prop="totalCycles" label="Смыкания" width="120" align="center" />
        <el-table-column prop="workHours" label="Наработка (часы)" width="130" align="center">
          <template #default="{ row }">
            {{ row.workHours?.toFixed(2) || '0.00' }}
          </template>
        </el-table-column>
        <el-table-column prop="to1Cycles" label="ТО-1" width="120" align="center">
          <template #default="{ row }">
            <span v-if="row.to1Cycles">
              <el-tag :type="row.allTimeTotalCycles >= row.to1Cycles ? 'danger' : 'info'" size="small">
                {{ row.to1Cycles.toLocaleString() }}
              </el-tag>
            </span>
            <span v-else class="text-gray-400">—</span>
          </template>
        </el-table-column>
        <el-table-column prop="to2Cycles" label="ТО-2" width="120" align="center">
          <template #default="{ row }">
            <span v-if="row.to2Cycles">
              <el-tag :type="row.allTimeTotalCycles >= row.to2Cycles ? 'danger' : 'info'" size="small">
                {{ row.to2Cycles.toLocaleString() }}
              </el-tag>
            </span>
            <span v-else class="text-gray-400">—</span>
          </template>
        </el-table-column>
        <el-table-column label="Ресурс" width="210" align="left">
          <template #default="{ row }">
            <div class="flex items-center gap-2">
              <div class="flex-shrink-0 w-4 flex justify-center">
                <el-icon v-if="row.remainingResource < 10000" color="#F56C6C" :size="15"><CircleCloseFilled /></el-icon>
                <el-icon v-else-if="row.remainingResource < 50000" color="#E6A23C" :size="15"><WarningFilled /></el-icon>
                <el-icon v-else color="#909399" :size="15"><CircleCheckFilled /></el-icon>
              </div>
              <div class="flex-1 text-xs leading-tight min-w-0">
                <div>{{ row.allTimeTotalCycles.toLocaleString() }} / {{ row.maxResourceCycles.toLocaleString() }}</div>
                <el-progress
                  :percentage="Math.min(100, Math.round(row.allTimeTotalCycles / row.maxResourceCycles * 100))"
                  :show-text="false"
                  :color="row.remainingResource < 10000 ? '#F56C6C' : row.remainingResource < 50000 ? '#E6A23C' : '#409EFF'"
                  :stroke-width="6"
                  class="mt-1"
                />
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="Износ" width="70" align="center">
          <template #default="{ row }">
            <span
              class="text-xs font-medium tabular-nums"
              :class="row.remainingResource < 10000 ? 'text-red-500' : row.remainingResource < 50000 ? 'text-yellow-500' : 'text-gray-400'"
            >
              {{ Math.min(100, Math.round(row.allTimeTotalCycles / row.maxResourceCycles * 100)) }}%
            </span>
          </template>
        </el-table-column>
        <el-table-column prop="remainingResource" label="Остаток" width="110" align="center">
          <template #default="{ row }">
            <el-tag :type="row.remainingResource < 10000 ? 'danger' : row.remainingResource < 50000 ? 'warning' : 'success'">
              {{ row.remainingResource.toLocaleString() }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Таблица: Наладчики -->
    <el-card v-loading="loading" v-if="reportType === 'Personnel'">
      <el-table :data="reportData?.personnelData || []" stripe style="width: 100%">
        <el-table-column prop="fullName" label="Наладчик" min-width="200" />
        <el-table-column prop="completedTasks" label="Выполнено заданий" width="140" align="center" />
        <el-table-column prop="workedShifts" label="Отработано смен" width="140" align="center" />
        <el-table-column prop="totalWorkSeconds" label="Время работы (ч)" width="140">
          <template #default="{ row }">
            {{ (row.totalWorkSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column prop="avgSetupTime" label="Ср. время наладки (мин)" width="160">
          <template #default="{ row }">
            {{ (row.avgSetupTime / 60).toFixed(1) }}
          </template>
        </el-table-column>
        <el-table-column label="Общее время наладки (ч)" width="160" align="center">
          <template #default="{ row }">
            {{ row.totalSetupSeconds ? (row.totalSetupSeconds / 3600).toFixed(1) : '—' }}
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Таблица: Наладчики по ТПА -->
    <el-card v-loading="loading" v-if="reportType === 'PersonnelByImm'">
      <el-table
        :data="reportData?.personnelByImmData || []"
        row-key="personnelId"
        stripe
        style="width: 100%"
      >
        <el-table-column type="expand">
          <template #default="{ row }">
            <div v-if="row.immBreakdown.length > 0">
              <div
                v-for="imm in row.immBreakdown"
                :key="imm.immId"
                class="flex items-center text-sm text-gray-500 py-1.5 border-b border-gray-100 last:border-0"
              >
                <div class="flex-1 pl-6" style="min-width: 200px">{{ imm.immName }}</div>
                <div class="text-center" style="width: 150px">{{ imm.completedTasks }}</div>
                <div class="text-center" style="width: 150px">{{ (imm.totalWorkSeconds / 3600).toFixed(2) }}</div>
                <div class="text-center" style="width: 170px">{{ imm.avgSetupTime ? (imm.avgSetupTime / 60).toFixed(1) : '—' }}</div>
                <div class="text-center" style="width: 160px">{{ imm.totalSetupSeconds ? (imm.totalSetupSeconds / 3600).toFixed(1) : '—' }}</div>
              </div>
            </div>
            <div v-else class="py-2 pl-6 text-gray-400 text-sm">Нет данных за период</div>
          </template>
        </el-table-column>
        <el-table-column prop="fullName" label="Наладчик" min-width="200" />
        <el-table-column prop="completedTasks" label="Выполнено заданий" width="150" align="center" />
        <el-table-column prop="workedShifts" label="Отработано смен" width="140" align="center" />
        <el-table-column label="Время работы (ч)" width="150" align="center">
          <template #default="{ row }">
            {{ (row.totalWorkSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column label="Ср. время наладки (мин)" width="170" align="center">
          <template #default="{ row }">
            {{ (row.avgSetupTime / 60).toFixed(1) }}
          </template>
        </el-table-column>
        <el-table-column label="Общее время наладки (ч)" width="160" align="center">
          <template #default="{ row }">
            {{ row.totalSetupSeconds ? (row.totalSetupSeconds / 3600).toFixed(1) : '—' }}
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Таблица: Пресс-формы по ТПА -->
    <el-card v-loading="loading" v-if="reportType === 'MoldsByImm'">
      <el-table
        :data="reportData?.moldsByImmData || []"
        row-key="moldId"
        stripe
        style="width: 100%"
      >
        <el-table-column type="expand">
          <template #default="{ row }">
            <div v-if="row.immBreakdown.length > 0">
              <div
                v-for="imm in row.immBreakdown"
                :key="imm.immId"
                class="flex items-center text-sm text-gray-500 py-1.5 border-b border-gray-100 last:border-0"
              >
                <div class="flex-1 pl-6" style="min-width: 200px">{{ imm.immName }}</div>
                <div class="text-center" style="width: 120px">{{ imm.totalCycles }}</div>
                <div class="text-center" style="width: 130px">{{ imm.workHours?.toFixed(2) || '0.00' }}</div>
                <div style="width: 630px"></div>
              </div>
            </div>
            <div v-else class="py-2 pl-6 text-gray-400 text-sm">Нет данных за период</div>
          </template>
        </el-table-column>
        <el-table-column prop="moldName" label="Пресс-форма" min-width="200" />
        <el-table-column prop="totalCycles" label="Смыканий" width="120" align="center" />
        <el-table-column prop="workHours" label="Наработка (часы)" width="130" align="center">
          <template #default="{ row }">
            {{ row.workHours?.toFixed(2) || '0.00' }}
          </template>
        </el-table-column>
        <el-table-column prop="to1Cycles" label="ТО-1" width="120" align="center">
          <template #default="{ row }">
            <span v-if="row.to1Cycles">
              <el-tag :type="row.allTimeTotalCycles >= row.to1Cycles ? 'danger' : 'info'" size="small">
                {{ row.to1Cycles.toLocaleString() }}
              </el-tag>
            </span>
            <span v-else class="text-gray-400">—</span>
          </template>
        </el-table-column>
        <el-table-column prop="to2Cycles" label="ТО-2" width="120" align="center">
          <template #default="{ row }">
            <span v-if="row.to2Cycles">
              <el-tag :type="row.allTimeTotalCycles >= row.to2Cycles ? 'danger' : 'info'" size="small">
                {{ row.to2Cycles.toLocaleString() }}
              </el-tag>
            </span>
            <span v-else class="text-gray-400">—</span>
          </template>
        </el-table-column>
        <el-table-column label="Ресурс" width="210" align="left">
          <template #default="{ row }">
            <div class="flex items-center gap-2">
              <div class="flex-shrink-0 w-4 flex justify-center">
                <el-icon v-if="row.remainingResource < 10000" color="#F56C6C" :size="15"><CircleCloseFilled /></el-icon>
                <el-icon v-else-if="row.remainingResource < 50000" color="#E6A23C" :size="15"><WarningFilled /></el-icon>
                <el-icon v-else color="#909399" :size="15"><CircleCheckFilled /></el-icon>
              </div>
              <div class="flex-1 text-xs leading-tight min-w-0">
                <div>{{ row.allTimeTotalCycles.toLocaleString() }} / {{ row.maxResourceCycles.toLocaleString() }}</div>
                <el-progress
                  :percentage="Math.min(100, Math.round(row.allTimeTotalCycles / row.maxResourceCycles * 100))"
                  :show-text="false"
                  :color="row.remainingResource < 10000 ? '#F56C6C' : row.remainingResource < 50000 ? '#E6A23C' : '#409EFF'"
                  :stroke-width="6"
                  class="mt-1"
                />
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="Износ" width="70" align="center">
          <template #default="{ row }">
            <span
              class="text-xs font-medium tabular-nums"
              :class="row.remainingResource < 10000 ? 'text-red-500' : row.remainingResource < 50000 ? 'text-yellow-500' : 'text-gray-400'"
            >
              {{ Math.min(100, Math.round(row.allTimeTotalCycles / row.maxResourceCycles * 100)) }}%
            </span>
          </template>
        </el-table-column>
        <el-table-column prop="remainingResource" label="Остаток" width="110" align="center">
          <template #default="{ row }">
            <el-tag :type="row.remainingResource < 10000 ? 'danger' : row.remainingResource < 50000 ? 'warning' : 'success'">
              {{ row.remainingResource.toLocaleString() }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useReportsStore } from '@/stores/reports'
import dayjs from 'dayjs'

const router = useRouter()
const reportsStore = useReportsStore()

const loading = ref(false)
const exporting = ref(false)
const dateRange = ref([dayjs().subtract(7, 'day').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')])
const reportType = ref('Molds')

const reportData = computed(() => reportsStore.assetsReport)

onMounted(async () => {
  await loadReport()
})

const loadReport = async () => {
  loading.value = true
  try {
    await reportsStore.loadAssetsReport({
      dateFrom: dateRange.value[0],
      dateTo: dateRange.value[1],
      reportType: reportType.value
    })
  } catch (error) {
    ElMessage.error('Ошибка формирования отчёта')
  } finally {
    loading.value = false
  }
}

const exportExcel = async () => {
  exporting.value = true
  try {
    await reportsStore.exportToExcel('assets', {
      dateFrom: dateRange.value[0],
      dateTo: dateRange.value[1],
      reportType: reportType.value
    })
  } catch (error) {
    ElMessage.error('Ошибка экспорта')
  } finally {
    exporting.value = false
  }
}

const goBack = () => {
  router.push('/reports')
}
</script>