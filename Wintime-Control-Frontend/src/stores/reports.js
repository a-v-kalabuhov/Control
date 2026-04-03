import { defineStore } from 'pinia'
import { reportsApi } from '@/api/reports'
import { ElMessage } from 'element-plus'

export const useReportsStore = defineStore('reports', {
  state: () => ({
    loading: false,
    currentReportType: null,
    dailyReport: null,
    equipmentReport: null,
    assetsReport: null,
    filters: {
      dateFrom: null,
      dateTo: null,
      date: null,
      immId: null,
      reportType: null
    }
  }),

  getters: {
    // Общая эффективность по отчёту оборудования
    overallEfficiency: (state) => {
      if (!state.equipmentReport?.immData?.length) return 0
      const total = state.equipmentReport.immData.reduce((sum, item) => sum + item.avgEfficiency, 0)
      return Math.round(total / state.equipmentReport.immData.length)
    },

    // Всего ТПА в отчёте
    totalImms: (state) => {
      if (!state.equipmentReport?.immData) return 0
      return state.equipmentReport.immData.length
    },

    // ТПА с эффективностью ниже 70%
    lowEfficiencyImms: (state) => {
      if (!state.equipmentReport?.immData) return []
      return state.equipmentReport.immData.filter(i => i.avgEfficiency < 70)
    }
  },

  actions: {
    // Загрузка дневного отчёта
    async loadDailyReport(params) {
      this.loading = true
      try {
        const response = await reportsApi.getDaily(params)
        this.dailyReport = response.data
        this.currentReportType = 'daily'
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка загрузки отчёта')
        return { success: false, message: error.message }
      } finally {
        this.loading = false
      }
    },

    // Загрузка отчёта оборудования
    async loadEquipmentReport(params) {
      this.loading = true
      try {
        const response = await reportsApi.getEquipment(params)
        this.equipmentReport = response.data
        this.currentReportType = 'equipment'
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка загрузки отчёта')
        return { success: false, message: error.message }
      } finally {
        this.loading = false
      }
    },

    // Загрузка отчёта активов
    async loadAssetsReport(params) {
      this.loading = true
      try {
        const response = await reportsApi.getAssets(params)
        this.assetsReport = response.data
        this.currentReportType = 'assets'
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка загрузки отчёта')
        return { success: false, message: error.message }
      } finally {
        this.loading = false
      }
    },

    // Экспорт в Excel
    async exportToExcel(reportType, params) {
      try {
        const response = await reportsApi.exportExcel({
          reportType,
          ...params
        })

        // Создание и скачивание файла
        const blob = new Blob([response.data], {
          type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
        })
        const url = window.URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = url
        link.download = `Report_${reportType}_${params.dateFrom}_${params.dateTo}.xlsx`
        link.click()
        window.URL.revokeObjectURL(url)

        ElMessage.success('Отчёт экспортирован в Excel')
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка экспорта в Excel')
        return { success: false, message: error.message }
      }
    },

    // Экспорт в PDF
    async exportToPdf(reportType, params) {
      try {
        const response = await reportsApi.exportPdf({
          reportType,
          ...params
        })

        const blob = new Blob([response.data], { type: 'application/pdf' })
        const url = window.URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = url
        link.download = `Report_${reportType}_${params.dateFrom}_${params.dateTo}.pdf`
        link.click()
        window.URL.revokeObjectURL(url)

        ElMessage.success('Отчёт экспортирован в PDF')
        return { success: true }
      } catch (error) {
        ElMessage.error('Ошибка экспорта в PDF')
        return { success: false, message: error.message }
      }
    },

    // Сброс данных
    clearReport() {
      this.dailyReport = null
      this.equipmentReport = null
      this.assetsReport = null
      this.currentReportType = null
    },

    // Установка фильтра
    setFilter(key, value) {
      this.filters[key] = value
    }
  }
})