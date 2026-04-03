import apiClient from './client'

export const downtimeApi = {
  getReasons(params) {
    return apiClient.get('/downtime-reasons', { params })
  },
  
  createReason(data) {
    return apiClient.post('/downtime-reasons', data)
  },
  
  updateReason(id, data) {
    return apiClient.put(`/downtime-reasons/${id}`, data)
  },
  
  deleteReason(id) {
    return apiClient.delete(`/downtime-reasons/${id}`)
  }
}