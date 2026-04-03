<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Производительность оборудования</h2>
        <p class="text-gray-600 mt-1">Сводный отчёт за период</p>
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
        <el-form-item>
          <el-button type="primary" @click="loadReport">Сформировать</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Сводные показатели -->
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4">
      <div class="card">
        <p class="text-sm text-gray-500">Всего ТПА</p>
        <p class="text-2xl font-bold text-gray-800">{{ reportsStore.totalImms }}</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Средняя эффективность</p>
        <p class="text-2xl font-bold" :class="efficiencyColor">{{ reportsStore.overallEfficiency }}%</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Проблемные ТПА</p>
        <p class="text-2xl font-bold text-red-600">{{ reportsStore.lowEfficiencyImms.length }}</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Всего циклов</p>
        <p class="text-2xl font-bold text-gray-800">{{ totalCycles }}</p>
      </div>
    </div>

    <!-- Диаграмма -->
    <el-card class="mb-4">
      <template #header>
        <span class="font-semibold">Загрузка ТПА по дням</span>
      </template>
      <BarChart :data="chartData" />
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
        <el-table-column label="Время работы (ч)" width="130">
          <template #default="{ row }">
            {{ (row.totalWorkSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column label="Время простоя (ч)" width="130">
          <template #default="{ row }">
            {{ (row.totalDowntimeSeconds / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column prop="totalCycles" label="Всего циклов" width="120" align="center" />
        <el-table-column label="Эффективность" width="150">
          <template #default="{ row }">
            <el-progress 
              :percentage="row.avgEfficiency" 
              :status="row.avgEfficiency >= 85 ? 'success' : row.avgEfficiency >= 70 ? 'warning' : 'exception'"
            />
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
import BarChart from '@/components/reports/BarChart.vue'
import dayjs from 'dayjs'

const router = useRouter()
const reportsStore = useReportsStore()

const loading = ref(false)
const exporting = ref(false)
const dateRange = ref([dayjs().subtract(7, 'day').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')])
const reportData = ref(null)

const totalCycles = computed(() => {
  if (!reportData.value?.immData) return 0
  return reportData.value.immData.reduce((sum, item) => sum + item.totalCycles, 0)
})

const efficiencyColor = computed(() => {
  const eff = reportsStore.overallEfficiency
  if (eff >= 85) return 'text-green-600'
  if (eff >= 70) return 'text-yellow-600'
  return 'text-red-600'
})

const chartData = computed(() => {
  // TODO: Преобразовать данные для диаграммы
  return []
})

onMounted(async () => {
  await loadReport()
})

const loadReport = async () => {
  loading.value = true
  try {
    await reportsStore.loadEquipmentReport({
      dateFrom: dateRange.value[0],
      dateTo: dateRange.value[1]
    })
    reportData.value = reportsStore.equipmentReport
  } catch (error) {
    ElMessage.error('Ошибка формирования отчёта')
  } finally {
    loading.value = false
  }
}

const exportExcel = async () => {
  exporting.value = true
  try {
    await reportsStore.exportToExcel('equipment', {
      dateFrom: dateRange.value[0],
      dateTo: dateRange.value[1]
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

const getSummaries = (param) => {
  const { columns, data } = param
  const sums = []
  
  columns.forEach((column, index) => {
    if (index === 0) {
      sums[index] = 'Итого:'
      return
    }
    
    if (['totalCycles'].includes(column.property)) {
      const values = data.map(item => Number(item[column.property]))
      sums[index] = values.reduce((prev, curr) => prev + curr, 0)
    }
  })
  
  return sums
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}
</style>