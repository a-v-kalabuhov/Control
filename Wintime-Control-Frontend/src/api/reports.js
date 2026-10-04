import apiClient from './client'

export const reportsApi = {
  // Отчёт "Картина рабочего дня"
  getDaily(params) {
    return apiClient.get('/reports/daily', { params })
  },

  // Отчёт "Производительность оборудования"
  getEquipment(params) {
    return apiClient.get('/reports/equipment', {
      params,
      // ASP.NET List<Guid> ждёт повтор параметра без индексов: immIds=a&immIds=b
      paramsSerializer: { indexes: null },
    })
  },

  // Отчёт "Активы цеха"
  getAssets(params) {
    return apiClient.get('/reports/assets', { params })
  },

  // Экспорт в Excel
  exportExcel(data) {
    return apiClient.post('/reports/export/excel', data, {
      responseType: 'blob'
    })
  },

  // Экспорт в PDF
  exportPdf(data) {
    return apiClient.post('/reports/export/pdf', data, {
      responseType: 'blob'
    })
  }
}