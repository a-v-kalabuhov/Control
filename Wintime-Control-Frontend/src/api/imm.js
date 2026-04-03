import apiClient from './client'

export const immApi = {
  getList(params) {
    return apiClient.get('/imm', { params })
  },
  
  getById(id) {
    return apiClient.get(`/imm/${id}`)
  },
  
  create(data) {
    return apiClient.post('/imm', data)
  },
  
  update(id, data) {
    return apiClient.put(`/imm/${id}`, data)
  },
  
  getStatus(id) {
    return apiClient.get(`/imm/${id}/status`)
  },
  
  getTelemetry(id, params) {
    return apiClient.get(`/imm/${id}/telemetry`, { params })
  },
  
  getStatistics(id, params) {
    return apiClient.get(`/imm/${id}/statistics`, { params })
  }
}