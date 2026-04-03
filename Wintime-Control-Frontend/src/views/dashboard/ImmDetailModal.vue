<template>
  <el-dialog
    v-model="visible"
    title="Детальная информация по ТПА"
    width="900px"
    :close-on-click-modal="false"
    @closed="onClosed"
  >
    <div v-loading="loading">
      <!-- Основная информация -->
      <el-descriptions :column="3" border class="mb-6">
        <el-descriptions-item label="Наименование">
          {{ imm?.name }}
        </el-descriptions-item>
        <el-descriptions-item label="Модель">
          {{ imm?.manufacturer }} {{ imm?.model }}
        </el-descriptions-item>
        <el-descriptions-item label="Статус">
          <ImmStatusBadge :status="imm?.status" />
        </el-descriptions-item>
        <el-descriptions-item label="Текущее задание">
          {{ imm?.currentMoldName || 'Нет задания' }}
        </el-descriptions-item>
        <el-descriptions-item label="Наладчик">
          {{ imm?.personnelName || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="Обновлено">
          {{ lastUpdateTime }}
        </el-descriptions-item>
      </el-descriptions>

      <!-- Графики телеметрии -->
      <div class="grid grid-cols-2 gap-4 mb-6">
        <el-card>
          <template #header>
            <span class="font-semibold">Температура зоны 1</span>
          </template>
          <CycleChart 
            :data="telemetry.temp_zone_1" 
            parameter-name="temp_zone_1"
            title="Температура (°C)"
          />
        </el-card>

        <el-card>
          <template #header>
            <span class="font-semibold">Температура зоны 2</span>
          </template>
          <CycleChart 
            :data="telemetry.temp_zone_2" 
            parameter-name="temp_zone_2"
            title="Температура (°C)"
          />
        </el-card>

        <el-card>
          <template #header>
            <span class="font-semibold">Давление впрыска</span>
          </template>
          <CycleChart 
            :data="telemetry.pressure_inject" 
            parameter-name="pressure_inject"
            title="Давление (бар)"
          />
        </el-card>

        <el-card>
          <template #header>
            <span class="font-semibold">Время цикла</span>
          </template>
          <CycleChart 
            :data="telemetry.cycle_time" 
            parameter-name="cycle_time"
            title="Время (сек)"
          />
        </el-card>
      </div>

      <!-- Статистика -->
      <el-card>
        <template #header>
          <span class="font-semibold">Статистика за смену</span>
        </template>
        <div class="grid grid-cols-4 gap-4">
          <div class="text-center">
            <div class="text-2xl font-bold text-gray-800">{{ statistics?.totalCycles || 0 }}</div>
            <div class="text-sm text-gray-500">Всего циклов</div>
          </div>
          <div class="text-center">
            <div class="text-2xl font-bold text-gray-800">{{ statistics?.avgCycleTime?.toFixed(2) || 0 }}</div>
            <div class="text-sm text-gray-500">Ср. время цикла (сек)</div>
          </div>
          <div class="text-center">
            <div class="text-2xl font-bold" :class="efficiencyColor">{{ statistics?.efficiency || 0 }}%</div>
            <div class="text-sm text-gray-500">Эффективность</div>
          </div>
          <div class="text-center">
            <div class="text-2xl font-bold text-gray-800">{{ statistics?.workHours || 0 }}</div>
            <div class="text-sm text-gray-500">Часов работы</div>
          </div>
        </div>
      </el-card>
    </div>

    <template #footer>
      <el-button @click="visible = false">Закрыть</el-button>
      <el-button type="primary" @click="refreshData" :loading="loading">
        <el-icon class="mr-1"><Refresh /></el-icon>
        Обновить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { dashboardApi } from '@/api/dashboard'
import { ElMessage } from 'element-plus'
import ImmStatusBadge from '@/components/dashboard/ImmStatusBadge.vue'
import CycleChart from '@/components/dashboard/CycleChart.vue'
import dayjs from 'dayjs'

const props = defineProps({
  immId: {
    type: String,
    required: true
  },
  modelValue: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:modelValue'])

const visible = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value)
})

const loading = ref(false)
const imm = ref(null)
const telemetry = ref({
  temp_zone_1: [],
  temp_zone_2: [],
  pressure_inject: [],
  cycle_time: []
})
const statistics = ref(null)

const lastUpdateTime = computed(() => {
  if (!imm.value?.lastUpdate) return '—'
  return dayjs(imm.value.lastUpdate).format('DD.MM.YYYY HH:mm:ss')
})

const efficiencyColor = computed(() => {
  const eff = statistics.value?.efficiency || 0
  if (eff >= 85) return 'text-green-600'
  if (eff >= 70) return 'text-yellow-600'
  return 'text-red-600'
})

watch(() => props.immId, async (newId) => {
  if (newId && visible.value) {
    await loadDetails()
  }
})

const loadDetails = async () => {
  loading.value = true
  try {
    // Загрузка статуса
    const statusResponse = await dashboardApi.getImmStatus(props.immId)
    imm.value = statusResponse.data

    // Загрузка телеметрии (последние 100 точек)
    const now = new Date()
    const from = new Date(now.getTime() - 30 * 60 * 1000) // 30 минут

    const telemetryParams = ['temp_zone_1', 'temp_zone_2', 'pressure_inject', 'cycle_time']
    
    for (const param of telemetryParams) {
      try {
        const response = await dashboardApi.getImmTelemetry(props.immId, {
          from: from.toISOString(),
          to: now.toISOString(),
          parameters: [param]
        })
        
        telemetry.value[param] = response.data.map(d => ({
          timestamp: d.timestamp,
          valueNumeric: d.valueNumeric
        }))
      } catch (error) {
        console.error(`Ошибка загрузки телеметрии ${param}:`, error)
      }
    }

    // Загрузка статистики
    try {
      const statsResponse = await dashboardApi.getImmStatistics(props.immId, {
        from: dayjs().startOf('day').toISOString(),
        to: dayjs().endOf('day').toISOString()
      })
      statistics.value = statsResponse.data
    } catch (error) {
      console.error('Ошибка загрузки статистики:', error)
    }

  } catch (error) {
    ElMessage.error('Ошибка загрузки данных ТПА')
  } finally {
    loading.value = false
  }
}

const refreshData = async () => {
  await loadDetails()
  ElMessage.success('Данные обновлены')
}

const onClosed = () => {
  imm.value = null
  telemetry.value = {
    temp_zone_1: [],
    temp_zone_2: [],
    pressure_inject: [],
    cycle_time: []
  }
  statistics.value = null
}
</script>