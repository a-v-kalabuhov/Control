import apiClient from './client'

export const moldsApi = {
  getList(params) {
    return apiClient.get('/molds', { params })
  },
  
  getById(id) {
    return apiClient.get(`/molds/${id}`)
  },
  
  create(data) {
    return apiClient.post('/molds', data)
  },
  
  update(id, data) {
    return apiClient.put(`/molds/${id}`, data)
  },
  
  getQr(id) {
    return apiClient.get(`/molds/${id}/qr`)
  }
}