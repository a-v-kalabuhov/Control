import apiClient from './client'

export const reportsApi = {
  getDaily(params) {
    return apiClient.get('/reports/daily', { params })
  },
  
  getEquipment(params) {
    return apiClient.get('/reports/equipment', { params })
  },
  
  getAssets(params) {
    return apiClient.get('/reports/assets', { params })
  },
  
  exportExcel(data) {
    return apiClient.post('/reports/export/excel', data, {
      responseType: 'blob'
    })
  },
  
  exportPdf(data) {
    return apiClient.post('/reports/export/pdf', data, {
      responseType: 'blob'
    })
  }
}