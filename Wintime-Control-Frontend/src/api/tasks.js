import apiClient from './client'

export const tasksApi = {
  getList(params) {
    return apiClient.get('/tasks', { params })
  },
  
  getById(id) {
    return apiClient.get(`/tasks/${id}`)
  },
  
  create(data) {
    return apiClient.post('/tasks', data)
  },
  
  update(id, data) {
    return apiClient.put(`/tasks/${id}`, data)
  },
  
  start(id, data) {
    return apiClient.post(`/tasks/${id}/start`, data)
  },
  
  complete(id, data) {
    return apiClient.post(`/tasks/${id}/complete`, data)
  },
  
  close(id, data) {
    return apiClient.post(`/tasks/${id}/close`, data)
  }
}