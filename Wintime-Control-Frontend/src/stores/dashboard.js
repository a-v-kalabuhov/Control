import { defineStore } from 'pinia'
import { dashboardApi } from '@/api/dashboard'
import { immApi } from '@/api/imm'
import { shiftsApi } from '@/api/shifts'

export const useDashboardStore = defineStore('dashboard', {
  state: () => ({
    imms: [],
    loading: false,
    lastUpdate: null,
    autoRefresh: true,
    refreshInterval: 10000, // 10 секунд
    selectedImmId: null,
    filters: {
      status: null,
      search: ''
    },
    shifts: [],
    shiftUtilization: null,    // { utilization, machineCount, from, to }
    shiftUtilizationLoading: false
  }),

  getters: {
    // Общее количество ТПА
    totalImms: (state) => state.imms.length,

    // ТПА в работе (Production)
    workingImms: (state) => state.imms.filter(i => i.status === 'Production'),

    // ТПА в наладке (Setup)
    setupImms: (state) => state.imms.filter(i => i.status === 'Setup'),

    // ТПА в простое (Downtime)
    downtimeImms: (state) => state.imms.filter(i => i.status === 'Downtime'),

    // ТПА без задания (NoTask)
    noTaskImms: (state) => state.imms.filter(i => i.status === 'NoTask'),

    // ТПА в незапланированном простое (Unplanned)
    unplannedImms: (state) => state.imms.filter(i => i.status === 'Unplanned'),

    // ТПА оффлайн (Offline)
    offlineImms: (state) => state.imms.filter(i => i.status === 'Offline'),

    // TODO(Task 8): DashboardView ещё ссылается на alarmImms — удалить геттер,
    // когда вьюха перейдёт на эффективные состояния (Alarm растворён в Downtime/Unplanned).
    alarmImms: (state) => state.imms.filter(i => i.status === 'Alarm'),

    // Мгновенная загрузка цеха: (Production + Setup) / все
    overallEfficiency: (state) => {
      if (state.imms.length === 0) return 0
      const active = state.imms.filter(i => i.status === 'Production' || i.status === 'Setup')
      return Math.round(active.length / state.imms.length * 100)
    },

    // Текущая смена или null
    currentShift: (state) => {
      if (state.shifts.length === 0) return null
      const now = new Date()
      const minutesNow = now.getHours() * 60 + now.getMinutes()
      return state.shifts.find(s => {
        const end = s.startMinutes + s.durationMinutes
        if (end <= 1440) {
          return minutesNow >= s.startMinutes && minutesNow < end
        }
        // смена переходит через полночь
        return minutesNow >= s.startMinutes || minutesNow < (end % 1440)
      }) ?? null
    },

    // Последняя завершённая смена (ближайшая к текущему моменту)
    lastCompletedShift: (state) => {
      if (state.shifts.length === 0) return null
      const now = new Date()
      const minutesNow = now.getHours() * 60 + now.getMinutes()
      // Ищем смену, которая закончилась позже всего, но раньше текущего момента
      let best = null
      let bestEnd = -1
      for (const s of state.shifts) {
        const end = (s.startMinutes + s.durationMinutes) % 1440
        // Сколько минут назад закончилась смена
        const minutesAgo = (minutesNow - end + 1440) % 1440
        if (minutesAgo > 0 && minutesAgo < 1440) {
          if (best === null || minutesAgo < (minutesNow - bestEnd + 1440) % 1440) {
            best = s
            bestEnd = end
          }
        }
      }
      return best
    },

    // Фильтрованный список ТПА
    filteredImms: (state) => {
      let result = state.imms

      if (state.filters.status) {
        result = result.filter(i => i.status === state.filters.status)
      }

      if (state.filters.search) {
        const search = state.filters.search.toLowerCase()
        result = result.filter(i => 
          i.name.toLowerCase().includes(search) ||
          i.model?.toLowerCase().includes(search) ||
          i.currentMoldName?.toLowerCase().includes(search)
        )
      }

      return result
    }
  },

  actions: {
    // Загрузка расписания смен
    async loadShifts() {
      try {
        const response = await shiftsApi.getShifts()
        this.shifts = response.data
      } catch (error) {
        console.error('Ошибка загрузки смен:', error)
      }
    },

    // Загрузка средней загрузки за смену
    async loadShiftUtilization(from, to) {
      this.shiftUtilizationLoading = true
      try {
        const response = await dashboardApi.getShiftUtilization(from, to)
        this.shiftUtilization = response.data
      } catch (error) {
        console.error('Ошибка загрузки загрузки смены:', error)
        this.shiftUtilization = null
      } finally {
        this.shiftUtilizationLoading = false
      }
    },

    // Загрузка списка ТПА
    async loadImms() {
      this.loading = true
      try {
        const response = await immApi.getList({ isActive: true })

        this.imms = response.data.map(imm => ({
          ...imm,
          rawStatus: imm.status,                       // сырой — про запас (BL-19)
          status: imm.effectiveStatus || 'Offline',    // на дашборде используем эффективный
          currentCycleTime: imm.avgCycleTime || 0
        }))
        this.lastUpdate = new Date()
        return { success: true }
      } catch (error) {
        console.error('Ошибка загрузки дашборда:', error)
        
        // Проверяем, является ли ошибка ошибкой авторизации
        const isAuthError = error?.isAuthError || error?.response?.status === 401
        
        if (isAuthError) {
          console.log('Ошибка авторизации при загрузке дашборда:', error.message)
          return { 
            success: false, 
            isAuthError: true,
            message: 'Сессия истекла. Пожалуйста, войдите снова.' 
          }
        }
        
        return { 
          success: false, 
          message: 'Ошибка загрузки данных' 
        }
      } finally {
        this.loading = false
      }
    },

    // Обновление статуса одного ТПА
    async refreshImmStatus(immId) {
      try {
        const response = await dashboardApi.getImmStatus(immId)
        const index = this.imms.findIndex(i => i.id === immId)
        if (index !== -1) {
          this.imms[index] = {
            ...this.imms[index],
            ...response.data,
            rawStatus: response.data.status,
            status: response.data.effectiveStatus || 'Offline'
          }
        }
      } catch (error) {
        console.error(`Ошибка обновления статуса ТПА ${immId}:`, error)
      }
    },

    // Полное обновление всех данных
    async refreshAll() {
      return await this.loadImms()
    },

    // Запуск автообновления
    startAutoRefresh() {
      this.autoRefresh = true
      this.refreshTimer = setInterval(() => {
        if (this.autoRefresh) {
          this.refreshAll()
        }
      }, this.refreshInterval)
    },

    // Остановка автообновления
    stopAutoRefresh() {
      this.autoRefresh = false
      if (this.refreshTimer) {
        clearInterval(this.refreshTimer)
        this.refreshTimer = null
      }
    },

    // Установка фильтра
    setFilter(key, value) {
      this.filters[key] = value
    },

    // Очистка фильтров
    clearFilters() {
      this.filters = {
        status: null,
        search: ''
      }
    },

    // Выбор ТПА для детального просмотра
    selectImm(immId) {
      this.selectedImmId = immId
    },

    // Сброс выбора
    clearSelection() {
      this.selectedImmId = null
    }
  }
})