import { defineStore } from 'pinia'
import { dashboardApi } from '@/api/dashboard'
import { immApi } from '@/api/imm'

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
    }
  }),

  getters: {
    // Общее количество ТПА
    totalImms: (state) => state.imms.length,

    // ТПА в работе (Auto)
    workingImms: (state) => state.imms.filter(i => i.status === 'Auto'),

    // ТПА в наладке (Manual)
    setupImms: (state) => state.imms.filter(i => i.status === 'Manual'),

    // ТПА в аварии (Alarm)
    alarmImms: (state) => state.imms.filter(i => i.status === 'Alarm'),

    // ТПА оффлайн (Offline)
    offlineImms: (state) => state.imms.filter(i => i.status === 'Offline'),

    // Общая эффективность цеха
    overallEfficiency: (state) => {
      const working = state.imms.filter(i => i.status === 'Auto')
      if (working.length === 0) return 0
      const avg = working.reduce((sum, i) => sum + (i.efficiency || 0), 0) / working.length
      return Math.round(avg)
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
    },

    // Есть ли активные аварии
    hasAlarms: (state) => state.alarmImms.length > 0
  },

  actions: {
    // Загрузка списка ТПА
    async loadImms() {
      this.loading = true
      try {
        const response = await immApi.getList({ isActive: true })
        
        // Для каждого ТПА загружаем статус
        const immsWithStatus = await Promise.all(
          response.data.map(async (imm) => {
            try {
              const statusResponse = await dashboardApi.getImmStatus(imm.id)
              return {
                ...imm,
                ...statusResponse.data,
                status: statusResponse.data.status || 'Offline'
              }
            } catch (error) {
              return {
                ...imm,
                status: 'Offline',
                currentCycleTime: 0,
                lastUpdate: null
              }
            }
          })
        )

        this.imms = immsWithStatus
        this.lastUpdate = new Date()
      } catch (error) {
        console.error('Ошибка загрузки дашборда:', error)
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
            status: response.data.status || 'Offline'
          }
        }
      } catch (error) {
        console.error(`Ошибка обновления статуса ТПА ${immId}:`, error)
      }
    },

    // Полное обновление всех данных
    async refreshAll() {
      await this.loadImms()
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