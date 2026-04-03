import apiClient from './client'

export const dashboardApi = {
  // Получить список всех ТПА со статусами
  getImmList(params) {
    return apiClient.get('/imm', { params })
  },

  // Получить статус конкретного ТПА
  getImmStatus(id) {
    return apiClient.get(`/imm/${id}/status`)
  },

  // Получить телеметрию ТПА за период
  getImmTelemetry(id, params) {
    return apiClient.get(`/imm/${id}/telemetry`, { params })
  },

  // Получить статистику по ТПА
  getImmStatistics(id, params) {
    return apiClient.get(`/imm/${id}/statistics`, { params })
  },

  // Получить активные задания
  getActiveTasks() {
    return apiClient.get('/tasks', { 
      params: { status: 'InProgress' } 
    })
  }
}