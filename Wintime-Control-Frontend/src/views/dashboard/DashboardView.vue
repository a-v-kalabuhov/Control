<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Мониторинг оборудования</h2>
        <p class="text-gray-600 mt-1">
          Текущее состояние всех ТПА в реальном времени
          <span v-if="dashboardStore.lastUpdate" class="text-sm ml-2">
            (Обновлено: {{ lastUpdateTime }})
          </span>
        </p>
      </div>
      <div class="flex items-center gap-2">
        <el-button 
          :icon="dashboardStore.autoRefresh ? 'VideoPause' : 'VideoPlay'"
          :type="dashboardStore.autoRefresh ? 'success' : 'info'"
          circle
          @click="toggleAutoRefresh"
        />
        <el-button 
          icon="Refresh" 
          circle 
          :loading="dashboardStore.loading"
          @click="refreshData"
        />
      </div>
    </div>

    <!-- Статистика цеха -->
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4 mb-6">
      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-blue-100 rounded-lg">
            <el-icon class="text-blue-600 text-xl"><Monitor /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Всего ТПА</p>
            <p class="text-2xl font-bold text-gray-800">{{ dashboardStore.totalImms }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-green-100 rounded-lg">
            <el-icon class="text-green-600 text-xl"><CircleCheck /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">В работе</p>
            <p class="text-2xl font-bold text-gray-800">{{ dashboardStore.workingImms.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-yellow-100 rounded-lg">
            <el-icon class="text-yellow-600 text-xl"><Clock /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Наладка</p>
            <p class="text-2xl font-bold text-gray-800">{{ dashboardStore.setupImms.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-red-100 rounded-lg">
            <el-icon class="text-red-600 text-xl"><Warning /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Авария</p>
            <p class="text-2xl font-bold text-gray-800">{{ dashboardStore.alarmImms.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-purple-100 rounded-lg">
            <el-icon class="text-purple-600 text-xl"><TrendCharts /></el-icon>
          </div>
          <div class="min-w-0">
            <p class="text-sm text-gray-500">Текущая загрузка</p>
            <div class="flex items-baseline gap-2 flex-wrap">
              <p class="text-2xl font-bold" :class="efficiencyColor">
                {{ instantUtilizationLabel }}
              </p>
              <span v-if="shiftEndedLabel" class="text-xs text-gray-400">{{ shiftEndedLabel }}</span>
            </div>
            <p v-if="shiftAvgLabel" class="text-xs text-gray-500 mt-0.5">
              {{ shiftAvgLabel }}
            </p>
            <p v-if="shiftLabel" class="text-xs text-gray-400 mt-0.5">{{ shiftLabel }}</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="Поиск">
          <el-input 
            v-model="searchQuery" 
            placeholder="Наименование, модель, ПФ"
            clearable
            prefix-icon="Search"
            class="w-64"
          />
        </el-form-item>
        <el-form-item label="Статус">
          <el-select v-model="statusFilter" placeholder="Все" clearable class="w-52">
            <el-option label="В работе" value="Auto" />
            <el-option label="Наладка" value="Manual" />
            <el-option label="Авария" value="Alarm" />
            <el-option label="Простой" value="Idle" />
            <el-option label="Оффлайн" value="Offline" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button @click="clearFilters">Сбросить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Плитки ТПА -->
    <div 
      v-loading="dashboardStore.loading"
      element-loading-text="Загрузка данных..."
    >
      <div 
        v-if="dashboardStore.filteredImms.length === 0"
        class="text-center py-12"
      >
        <el-empty description="Нет данных для отображения" />
      </div>

      <div 
        v-else
        class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4"
      >
        <ImmCard
          v-for="imm in dashboardStore.filteredImms"
          :key="imm.id"
          :imm="imm"
          @click="openImmDetail(imm)"
        />
      </div>
    </div>

    <!-- Модальное окно деталей -->
    <ImmDetailModal
      v-model="detailModalVisible"
      :imm-id="selectedImmId"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useDashboardStore } from '@/stores/dashboard'
import { useAuthStore } from '@/stores/auth'
import { ElMessage } from 'element-plus'
import ImmCard from '@/components/dashboard/ImmCard.vue'
import ImmDetailModal from './ImmDetailModal.vue'
import dayjs from 'dayjs'

const dashboardStore = useDashboardStore()

const detailModalVisible = ref(false)
const selectedImmId = ref(null)
const searchQuery = ref('')
const statusFilter = ref('')

const lastUpdateTime = computed(() => {
  if (!dashboardStore.lastUpdate) return '—'
  return dayjs(dashboardStore.lastUpdate).format('HH:mm:ss')
})

const efficiencyColor = computed(() => {
  const eff = dashboardStore.overallEfficiency
  if (eff >= 85) return 'text-green-600'
  if (eff >= 70) return 'text-yellow-600'
  return 'text-red-600'
})

// Возвращает { from, to } для смены относительно переданной даты
function shiftBounds(shift, date) {
  const base = new Date(date)
  base.setHours(0, 0, 0, 0)
  const from = new Date(base.getTime() + shift.startMinutes * 60000)
  const to   = new Date(from.getTime() + shift.durationMinutes * 60000)
  return { from, to }
}

// Метка мгновенной загрузки с учётом контекста смены
const instantUtilizationLabel = computed(() => {
  const eff = dashboardStore.overallEfficiency
  const hasActive = dashboardStore.imms.some(i => i.status === 'Auto' || i.status === 'Manual')
  if (!dashboardStore.currentShift && !hasActive) return '—'
  return `${eff}%`
})

// "(смена закончилась)" — показываем когда нет текущей смены, но есть активные станки
const shiftEndedLabel = computed(() => {
  if (dashboardStore.currentShift) return null
  const hasActive = dashboardStore.imms.some(i => i.status === 'Auto' || i.status === 'Manual')
  return hasActive ? '(смена закончилась)' : null
})

// Метка средней загрузки за смену
const shiftAvgLabel = computed(() => {
  const u = dashboardStore.shiftUtilization
  if (!u) return null
  return `Ср. за смену: ${u.utilization}%`
})

// Название текущей/последней смены
const shiftLabel = computed(() => {
  const shift = dashboardStore.currentShift ?? dashboardStore.lastCompletedShift
  if (!shift) return null
  const bounds = shiftBounds(shift, new Date())
  const fmt = (d) => dayjs(d).format('HH:mm')
  const prefix = dashboardStore.currentShift ? 'Смена' : 'Последняя смена'
  return `${prefix}: ${fmt(bounds.from)}–${fmt(bounds.to)}`
})

async function loadShiftUtilization() {
  const shift = dashboardStore.currentShift ?? dashboardStore.lastCompletedShift
  if (!shift) return

  const referenceDate = new Date()
  if (!dashboardStore.currentShift) {
    // Для последней завершённой смены: если она ещё не начиналась сегодня — берём вчера
    const bounds = shiftBounds(shift, referenceDate)
    if (bounds.from > referenceDate) {
      referenceDate.setDate(referenceDate.getDate() - 1)
    }
  }

  const { from, to } = shiftBounds(shift, referenceDate)
  const effectiveTo = dashboardStore.currentShift ? new Date() : to
  await dashboardStore.loadShiftUtilization(from, effectiveTo)
}

onMounted(async () => {
  await Promise.all([dashboardStore.loadImms(), dashboardStore.loadShifts()])
  await loadShiftUtilization()
  dashboardStore.startAutoRefresh()
})

onUnmounted(() => {
  dashboardStore.stopAutoRefresh()
})

const toggleAutoRefresh = () => {
  if (dashboardStore.autoRefresh) {
    dashboardStore.stopAutoRefresh()
    ElMessage.info('Автообновление остановлено')
  } else {
    dashboardStore.startAutoRefresh()
    ElMessage.success('Автообновление включено')
  }
}

const refreshData = async () => {
  const result = await dashboardStore.refreshAll()
  await loadShiftUtilization()

  if (result.success) {
    ElMessage.success('Данные обновлены')
  } else if (result.isAuthError) {
    // Ошибка авторизации - показываем сообщение, но не перенаправляем сразу
    ElMessage.warning({
      message: result.message || 'Сессия истекла. Пожалуйста, войдите снова.',
      duration: 5000,
      showClose: true,
      onClose: () => {
        // После закрытия сообщения предлагаем пользователю перейти на страницу логина
        // Но только если пользователь явно закрыл сообщение
        const authStore = useAuthStore()
        authStore.clearAuth()
      }
    })
  } else {
    ElMessage.error(result.message || 'Ошибка обновления данных')
  }
}

const openImmDetail = (imm) => {
  selectedImmId.value = imm.id
  detailModalVisible.value = true
}

const clearFilters = () => {
  searchQuery.value = ''
  statusFilter.value = ''
  dashboardStore.clearFilters()
}

// Синхронизация фильтров с хранилищем
watch(searchQuery, (value) => {
  dashboardStore.setFilter('search', value)
})

watch(statusFilter, (value) => {
  dashboardStore.setFilter('status', value)
})
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}
</style>