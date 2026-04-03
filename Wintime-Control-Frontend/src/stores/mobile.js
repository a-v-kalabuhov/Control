import { defineStore } from 'pinia'
import { mobileApi } from '@/api/mobile'
import { ElMessage } from 'element-plus'

export const useMobileStore = defineStore('mobile', {
  state: () => ({
    tasks: [],
    loading: false,
    selectedTask: null,
    scannerActive: false,
    scannedData: null,
    downtimeReasons: [],
    activeDowntime: null,
    filters: {
      status: null,
      search: ''
    }
  }),

  getters: {
    // Активные задания
    activeTasks: (state) => state.tasks.filter(t => 
      ['Issued', 'InProgress'].includes(t.status)
    ),

    // Завершённые задания
    completedTasks: (state) => state.tasks.filter(t => 
      ['Completed', 'Closed'].includes(t.status)
    ),

    // Задания в работе
    inProgressTasks: (state) => state.tasks.filter(t => t.status === 'InProgress'),

    // Фильтрованный список
    filteredTasks: (state) => {
      let result = state.tasks

      if (state.filters.status) {
        result = result.filter(t => t.status === state.filters.status)
      }

      if (state.filters.search) {
        const search = state.filters.search.toLowerCase()
        result = result.filter(t => 
          t.immName?.toLowerCase().includes(search) ||
          t.moldName?.toLowerCase().includes(search)
        )
      }

      return result
    }
  },

  actions: {
    // Загрузка моих заданий
    async loadMyTasks() {
      this.loading = true
      try {
        const response = await mobileApi.getMyTasks({ 
          status: this.filters.status || undefined 
        })
        this.tasks = response.data
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка загрузки заданий')
        return { success: false, message: error.message }
      } finally {
        this.loading = false
      }
    },

    // Выбор задания
    selectTask(task) {
      this.selectedTask = task
    },

    // Сброс выбора
    clearSelection() {
      this.selectedTask = null
    },

    // Активация сканера
    activateScanner() {
      this.scannerActive = true
      this.scannedData = null
    },

    // Деактивация сканера
    deactivateScanner() {
      this.scannerActive = false
      this.scannedData = null
    },

    // Обработка отсканированного QR
    processScannedData(data) {
      this.scannedData = data
      this.scannerActive = false
    },

    // Загрузка причин простоев
    async loadDowntimeReasons() {
      try {
        const response = await mobileApi.getDowntimeReasons({ isActive: true })
        this.downtimeReasons = response.data
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка загрузки причин простоев')
        return { success: false }
      }
    },

    // Начало простоя
    async startDowntime(immId, reasonId) {
      try {
        await mobileApi.startDowntime({
          immId,
          reasonId,
          startTime: new Date().toISOString()
        })
        this.activeDowntime = {
          immId,
          reasonId,
          startTime: new Date()
        }
        ElMessage.success('Простой начат')
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка начала простоя')
        return { success: false }
      }
    },

    // Завершение простоя
    async stopDowntime(immId) {
      try {
        await mobileApi.stopDowntime({
          immId,
          endTime: new Date().toISOString()
        })
        this.activeDowntime = null
        ElMessage.success('Простой завершён')
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка завершения простоя')
        return { success: false }
      }
    },

    // Установка фильтра
    setFilter(key, value) {
      this.filters[key] = value
    },

    // Сброс фильтров
    clearFilters() {
      this.filters = {
        status: null,
        search: ''
      }
    }
  }
})