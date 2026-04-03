import apiClient from './client'

export const templatesApi = {
  getList() {
    return apiClient.get('/templates')
  },
  
  getById(id) {
    return apiClient.get(`/templates/${id}`)
  },
  
  create(data) {
    return apiClient.post('/templates', data)
  },
  
  update(id, data) {
    return apiClient.put(`/templates/${id}`, data)
  },
  
  delete(id) {
    return apiClient.delete(`/templates/${id}`)
  }
}