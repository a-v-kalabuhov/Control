import apiClient from './client'

export const productTypesApi = {
  getList(params) {
    return apiClient.get('/producttypes', { params })
  },
  getById(id) {
    return apiClient.get(`/producttypes/${id}`)
  },
  create(data) {
    return apiClient.post('/producttypes', data)
  },
  update(id, data) {
    return apiClient.put(`/producttypes/${id}`, data)
  }
}
