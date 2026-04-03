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
        <el-table-column prop="workHours" label="Наработка (часы)" width="130">
          <template #default="{ row }">
            {{ row.workHours?.toFixed(2) || '0.00' }}
          </template>
        </el-table-column>
        <el-table-column prop="remainingResource" label="Остаток ресурса" width="140">
          <template #default="{ row }">
            <el-tag :type="row.remainingResource < 10000 ? 'danger' : row.remainingResource < 50000 ? 'warning' : 'success'">
              {{ row.remainingResource }}
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
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
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
const reportData = ref(null)

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
    reportData.value = reportsStore.assetsReport
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