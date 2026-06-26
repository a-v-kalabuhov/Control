import { defineStore } from 'pinia'
import { mobileApi } from '@/api/mobile'
import { shiftsApi } from '@/api/shifts'
import { computeShiftBoundary } from '@/constants/shift'
import { ElMessage } from 'element-plus'

export const useMobileStore = defineStore('mobile', {
  state: () => ({
    // Разделы заданий наладчика
    currentShiftTasks: [],   // задания текущей (или ближайшей) смены, любой статус
    unfinishedTasks: [],     // незавершённые задания прошедших смен
    archiveTasks: [],        // завершённые/закрытые задания прошедших смен (страница)
    archiveTotal: 0,
    archivePage: 1,
    archivePageSize: 20,

    search: '',
    shiftSchedule: [],

    loading: false,
    selectedTask: null,
    scannerActive: false,
    scannedData: null,
    downtimeReasons: [],
    activeDowntime: null
  }),

  getters: {
    // Плоский список всех загруженных заданий — для поиска по id (сканер и пр.)
    tasks: (state) => [
      ...state.currentShiftTasks,
      ...state.unfinishedTasks,
      ...state.archiveTasks
    ],

    // Задание в состоянии наладки (не более одного)
    setupTask: (state) =>
      [...state.currentShiftTasks, ...state.unfinishedTasks].find(t => t.status === 'Setup') ?? null
  },

  actions: {
    // Загрузка расписания смен (для вычисления границы смены)
    async loadShifts() {
      try {
        const res = await shiftsApi.getShifts()
        this.shiftSchedule = res.data ?? []
      } catch {
        this.shiftSchedule = []
      }
    },

    // Загрузка моих заданий, разложенных по разделам
    async loadMyTasks() {
      this.loading = true
      try {
        const boundary = computeShiftBoundary(this.shiftSchedule)
        const response = await mobileApi.getMyTasks({
          boundary: boundary.toISOString(),
          search: this.search || undefined,
          archivePage: this.archivePage,
          archivePageSize: this.archivePageSize
        })
        const data = response.data ?? {}
        this.currentShiftTasks = data.currentShift ?? []
        this.unfinishedTasks = data.unfinished ?? []
        this.archiveTasks = data.archive?.items ?? []
        this.archiveTotal = data.archive?.total ?? 0
        this.archivePage = data.archive?.page ?? this.archivePage
        this.archivePageSize = data.archive?.pageSize ?? this.archivePageSize
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка загрузки заданий')
        return { success: false, message: error.message }
      } finally {
        this.loading = false
      }
    },

    // Смена страницы архива
    async setArchivePage(page) {
      this.archivePage = page
      await this.loadMyTasks()
    },

    // Установка поиска — сбрасывает архив на первую страницу
    async applySearch(value) {
      this.search = value
      this.archivePage = 1
      await this.loadMyTasks()
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
    }
  }
})
