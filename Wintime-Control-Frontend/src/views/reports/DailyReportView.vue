<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Картина рабочего дня</h2>
        <p class="text-gray-600 mt-1">Детальный отчёт по сменам и ТПА</p>
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
        <el-form-item label="Дата">
          <el-date-picker
            v-model="filters.date"
            type="date"
            placeholder="Выберите дату"
            value-format="YYYY-MM-DD"
            class="w-48"
          />
        </el-form-item>
        <el-form-item label="Смена">
          <el-select v-model="filters.shiftId" placeholder="Вся смена" clearable class="w-44">
            <el-option
              v-for="shift in shifts"
              :key="shift.id"
              :label="`Смена ${shift.number} (${shift.startTime}–${shift.endTime})`"
              :value="shift.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="ТПА">
          <el-select v-model="filters.immId" placeholder="Все" clearable class="w-48">
            <el-option
              v-for="imm in imms"
              :key="imm.id"
              :label="imm.name"
              :value="imm.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadReport">Сформировать</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- График Ганта -->
    <el-card class="mb-4" v-if="reportData?.immData?.length">
      <template #header>
        <span class="font-semibold">График работы ТПА (Гант)</span>
      </template>
      <GanttChart :data="reportData.immData" />
    </el-card>

    <!-- Таблица -->
    <el-card v-loading="loading">
      <el-table 
        :data="reportData?.immData || []" 
        stripe 
        style="width: 100%"
        :summary-method="getSummaries"
        show-summary
      >
        <el-table-column prop="immName" label="ТПА" width="150" fixed />
        <el-table-column prop="moldName" label="Пресс-форма" min-width="180" />
        <el-table-column prop="planQuantity" label="План" width="80" align="center" />
        <el-table-column prop="actualQuantity" label="Факт" width="80" align="center" />
        <el-table-column prop="cycleCount" label="Смыкания" width="90" align="center" />
        <el-table-column label="Работа (ч)" width="100">
          <template #default="{ row }">
            {{ (row.workTimeSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column label="Наладка (ч)" width="100">
          <template #default="{ row }">
            {{ (row.setupSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column label="Простой (ч)" width="100">
          <template #default="{ row }">
            {{ (row.downtimeSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column prop="avgCycleTime" label="Ср. цикл (сек)" width="100" align="center" />
        <el-table-column label="Эффективность" width="120">
          <template #default="{ row }">
            <el-tag :type="getEfficiencyType(row.efficiency)">
              {{ row.efficiency.toFixed(1) }}%
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="Расход сырья (кг)" width="130">
          <template #default="{ row }">
            {{ row.rawMaterialKg?.toFixed(2) || '0.00' }}
          </template>
        </el-table-column>
        <el-table-column label="Простои по причинам" min-width="200">
          <template #default="{ row }">
            <div v-if="row.downtimeDetails?.length" class="text-xs">
              <div v-for="(detail, idx) in row.downtimeDetails" :key="idx" class="text-gray-600">
                {{ detail.reasonName }}: {{ (detail.durationSeconds / 60).toFixed(0) }} мин
              </div>
            </div>
            <span v-else class="text-gray-400">—</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useReportsStore } from '@/stores/reports'
import { immApi } from '@/api/imm'
import { shiftsApi } from '@/api/shifts'
import GanttChart from '@/components/reports/GanttChart.vue'
import dayjs from 'dayjs'

const router = useRouter()
const reportsStore = useReportsStore()

const loading = ref(false)
const exporting = ref(false)
const imms = ref([])
const shifts = ref([])
const reportData = ref(null)

const filters = reactive({
  date: dayjs().format('YYYY-MM-DD'),
  shiftId: null,
  immId: null
})

onMounted(async () => {
  await Promise.all([loadImms(), loadShifts()])
})

const loadImms = async () => {
  try {
    const response = await immApi.getList({ isActive: true })
    imms.value = response.data
  } catch {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}

const loadShifts = async () => {
  try {
    const response = await shiftsApi.getShifts()
    shifts.value = response.data ?? []
  } catch {
    shifts.value = []
  }
}

const loadReport = async () => {
  loading.value = true
  try {
    await reportsStore.loadDailyReport({
      date: filters.date,
      shiftId: filters.shiftId || undefined,
      immId: filters.immId || undefined
    })
    reportData.value = reportsStore.dailyReport
  } catch {
    ElMessage.error('Ошибка формирования отчёта')
  } finally {
    loading.value = false
  }
}

const exportExcel = async () => {
  exporting.value = true
  try {
    await reportsStore.exportToExcel('daily', {
      dateFrom: filters.date,
      dateTo: filters.date,
      immIds: filters.immId ? [filters.immId] : null,
      shiftId: filters.shiftId || undefined
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

const getEfficiencyType = (efficiency) => {
  if (efficiency >= 85) return 'success'
  if (efficiency >= 70) return 'warning'
  return 'danger'
}

const getSummaries = (param) => {
  const { columns, data } = param
  const sums = []
  
  columns.forEach((column, index) => {
    if (index === 0) {
      sums[index] = 'Итого:'
      return
    }
    
    if (['planQuantity', 'actualQuantity', 'cycleCount'].includes(column.property)) {
      const values = data.map(item => Number(item[column.property]))
      sums[index] = values.reduce((prev, curr) => prev + curr, 0)
    }
  })
  
  return sums
}
</script>