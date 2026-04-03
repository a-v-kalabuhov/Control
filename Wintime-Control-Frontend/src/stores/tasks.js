import { defineStore } from 'pinia'
import { tasksApi } from '@/api/tasks'
import { ElMessage } from 'element-plus'

export const useTasksStore = defineStore('tasks', {
  state: () => ({
    tasks: [],
    loading: false,
    filters: {
      status: null,
      immId: null,
      personnelId: null,
      dateFrom: null,
      dateTo: null,
      search: ''
    },
    pagination: {
      page: 1,
      pageSize: 20,
      total: 0
    },
    selectedTask: null
  }),

  getters: {
    // Задания в работе
    inProgressTasks: (state) => state.tasks.filter(t => t.status === 'InProgress'),

    // Задания в черновиках
    draftTasks: (state) => state.tasks.filter(t => t.status === 'Draft'),

    // Задания с просрочкой
    overdueTasks: (state) => {
      const now = new Date()
      return state.tasks.filter(t => {
        if (t.status === 'Completed' || t.status === 'Closed') return false
        if (!t.issuedAt) return false
        const issuedDate = new Date(t.issuedAt)
        const hoursDiff = (now - issuedDate) / (1000 * 60 * 60)
        return hoursDiff > 12 // Более 12 часов в работе
      })
    },

    // Общая эффективность выполнения
    overallProgress: (state) => {
      const activeTasks = state.tasks.filter(t => 
        t.status === 'InProgress' || t.status === 'Issued'
      )
      if (activeTasks.length === 0) return 0
      
      const totalProgress = activeTasks.reduce((sum, t) => sum + (t.progressPercent || 0), 0)
      return Math.round(totalProgress / activeTasks.length)
    },

    // Фильтрованный список
    filteredTasks: (state) => {
      let result = state.tasks

      if (state.filters.status) {
        result = result.filter(t => t.status === state.filters.status)
      }

      if (state.filters.immId) {
        result = result.filter(t => t.immId === state.filters.immId)
      }

      if (state.filters.personnelId) {
        result = result.filter(t => t.personnelId === state.filters.personnelId)
      }

      if (state.filters.search) {
        const search = state.filters.search.toLowerCase()
        result = result.filter(t => 
          t.immName?.toLowerCase().includes(search) ||
          t.moldName?.toLowerCase().includes(search) ||
          t.personnelName?.toLowerCase().includes(search)
        )
      }

      return result
    }
  },

  actions: {
    // Загрузка заданий
    async loadTasks() {
      this.loading = true
      try {
        const params = {
          ...this.filters,
          dateFrom: this.filters.dateFrom ? new Date(this.filters.dateFrom).toISOString() : null,
          dateTo: this.filters.dateTo ? new Date(this.filters.dateTo).toISOString() : null
        }

        // Очистка пустых параметров
        Object.keys(params).forEach(key => {
          if (params[key] === null || params[key] === undefined || params[key] === '') {
            delete params[key]
          }
        })

        const response = await tasksApi.getList(params)
        this.tasks = response.data
        this.pagination.total = response.data.length
      } catch (error) {
        ElMessage.error('Ошибка загрузки заданий')
        console.error('Load tasks error:', error)
      } finally {
        this.loading = false
      }
    },

    // Создание задания
    async createTask(data) {
      try {
        const response = await tasksApi.create(data)
        await this.loadTasks()
        ElMessage.success('Задание создано')
        return { success: true,  response.data }
      } catch (error) {
        const message = error.response?.data?.message || 'Ошибка создания задания'
        ElMessage.error(message)
        return { success: false, message }
      }
    },

    // Обновление задания
    async updateTask(id, data) {
      try {
        await tasksApi.update(id, data)
        await this.loadTasks()
        ElMessage.success('Задание обновлено')
        return { success: true }
      } catch (error) {
        const message = error.response?.data?.message || 'Ошибка обновления задания'
        ElMessage.error(message)
        return { success: false, message }
      }
    },

    // Начало выполнения
    async startTask(id, data) {
      try {
        await tasksApi.start(id, data)
        await this.loadTasks()
        ElMessage.success('Задание начато')
        return { success: true }
      } catch (error) {
        const message = error.response?.data?.message || 'Ошибка начала задания'
        ElMessage.error(message)
        return { success: false, message }
      }
    },

    // Завершение задания
    async completeTask(id, data) {
      try {
        await tasksApi.complete(id, data)
        await this.loadTasks()
        ElMessage.success('Задание завершено')
        return { success: true }
      } catch (error) {
        const message = error.response?.data?.message || 'Ошибка завершения задания'
        ElMessage.error(message)
        return { success: false, message }
      }
    },

    // Закрытие задания
    async closeTask(id, data) {
      try {
        await tasksApi.close(id, data)
        await this.loadTasks()
        ElMessage.success('Задание закрыто')
        return { success: true }
      } catch (error) {
        const message = error.response?.data?.message || 'Ошибка закрытия задания'
        ElMessage.error(message)
        return { success: false, message }
      }
    },

    // Удаление задания
    async deleteTask(id) {
      try {
        await tasksApi.delete(id)
        await this.loadTasks()
        ElMessage.success('Задание удалено')
        return { success: true }
      } catch (error) {
        const message = error.response?.data?.message || 'Ошибка удаления задания'
        ElMessage.error(message)
        return { success: false, message }
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
        immId: null,
        personnelId: null,
        dateFrom: null,
        dateTo: null,
        search: ''
      }
    },

    // Выбор задания
    selectTask(task) {
      this.selectedTask = task
    },

    // Сброс выбора
    clearSelection() {
      this.selectedTask = null
    }
  }
})