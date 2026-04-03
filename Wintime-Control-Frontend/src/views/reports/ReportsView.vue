<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6">
      <h2 class="text-2xl font-bold text-gray-800">Отчёты</h2>
      <p class="text-gray-600 mt-1">Аналитические отчёты по производству с экспортом в Excel</p>
    </div>

    <!-- Выбор типа отчёта -->
    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Выберите тип отчёта</span>
      </template>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div 
          class="card cursor-pointer hover:shadow-lg transition-all"
          :class="selectedType === 'daily' ? 'ring-2 ring-primary-500' : ''"
          @click="selectedType = 'daily'"
        >
          <div class="flex items-center gap-4">
            <div class="p-3 bg-blue-100 rounded-lg">
              <el-icon class="text-blue-600 text-2xl"><Calendar /></el-icon>
            </div>
            <div>
              <h3 class="font-semibold text-gray-800">Картина рабочего дня</h3>
              <p class="text-sm text-gray-500">За смену по каждому ТПА</p>
            </div>
          </div>
        </div>

        <div 
          class="card cursor-pointer hover:shadow-lg transition-all"
          :class="selectedType === 'equipment' ? 'ring-2 ring-primary-500' : ''"
          @click="selectedType = 'equipment'"
        >
          <div class="flex items-center gap-4">
            <div class="p-3 bg-green-100 rounded-lg">
              <el-icon class="text-green-600 text-2xl"><TrendCharts /></el-icon>
            </div>
            <div>
              <h3 class="font-semibold text-gray-800">Производительность оборудования</h3>
              <p class="text-sm text-gray-500">За период по цеху</p>
            </div>
          </div>
        </div>

        <div 
          class="card cursor-pointer hover:shadow-lg transition-all"
          :class="selectedType === 'assets' ? 'ring-2 ring-primary-500' : ''"
          @click="selectedType = 'assets'"
        >
          <div class="flex items-center gap-4">
            <div class="p-3 bg-purple-100 rounded-lg">
              <el-icon class="text-purple-600 text-2xl"><Box /></el-icon>
            </div>
            <div>
              <h3 class="font-semibold text-gray-800">Активы цеха</h3>
              <p class="text-sm text-gray-500">Пресс-формы и наладчики</p>
            </div>
          </div>
        </div>
      </div>

      <div class="mt-4 flex justify-end">
        <el-button 
          type="primary" 
          :disabled="!selectedType"
          @click="openReport"
        >
          Открыть отчёт
        </el-button>
      </div>
    </el-card>

    <!-- Последние отчёты (быстрый доступ) -->
    <el-card>
      <template #header>
        <span class="font-semibold">Последние отчёты</span>
      </template>

      <el-table :data="recentReports" stripe style="width: 100%">
        <el-table-column prop="type" label="Тип" width="200">
          <template #default="{ row }">
            <el-tag :type="getReportTypeTag(row.type)">
              {{ getReportTypeLabel(row.type) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="dateFrom" label="Период" width="200">
          <template #default="{ row }">
            {{ row.dateFrom }} — {{ row.dateTo }}
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="Создан" width="180" />
        <el-table-column label="Действия" width="150">
          <template #default="{ row }">
            <el-button size="small" @click="reopenReport(row)">Открыть</el-button>
            <el-button size="small" type="success" @click="downloadReport(row)">
              <el-icon><Download /></el-icon>
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const selectedType = ref(null)

const recentReports = ref([
  { type: 'daily', dateFrom: '2026-02-26', dateTo: '2026-02-26', createdAt: '26.02.2026 18:30' },
  { type: 'equipment', dateFrom: '2026-02-01', dateTo: '2026-02-28', createdAt: '28.02.2026 17:00' },
  { type: 'assets', dateFrom: '2026-02-01', dateTo: '2026-02-28', createdAt: '28.02.2026 16:45' }
])

const openReport = () => {
  if (!selectedType.value) return
  router.push(`/reports/${selectedType.value}`)
}

const reopenReport = (report) => {
  router.push(`/reports/${report.type}?from=${report.dateFrom}&to=${report.dateTo}`)
}

const downloadReport = (report) => {
  // TODO: Реализовать скачивание из истории
  console.log('Download:', report)
}

const getReportTypeLabel = (type) => {
  const labels = {
    daily: 'Картина дня',
    equipment: 'Производительность',
    assets: 'Активы'
  }
  return labels[type] || type
}

const getReportTypeTag = (type) => {
  const tags = {
    daily: 'primary',
    equipment: 'success',
    assets: 'warning'
  }
  return tags[type] || 'info'
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4 border border-gray-200;
}
</style>