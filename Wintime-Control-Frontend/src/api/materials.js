import apiClient from './client'

// Справочник материалов — пластики и красители (PZP-22).
export const materialsApi = {
  getList(params) {
    return apiClient.get('/materials', { params })
  },
  getById(id) {
    return apiClient.get(`/materials/${id}`)
  },
  create(data) {
    return apiClient.post('/materials', data)
  },
  update(id, data) {
    return apiClient.put(`/materials/${id}`, data)
  },
  remove(id) {
    return apiClient.delete(`/materials/${id}`)
  },
  suggestManufacturers(search) {
    return apiClient.get('/materials/suggestions/manufacturers', { params: { search } })
  },
  suggestColors(search) {
    return apiClient.get('/materials/suggestions/colors', { params: { search } })
  }
}
