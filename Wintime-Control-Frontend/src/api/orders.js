import apiClient from './client'

export const ordersApi = {
  getList(params) {
    return apiClient.get('/orders', { params })
  },
  getById(id) {
    return apiClient.get(`/orders/${id}`)
  },
  create(data) {
    return apiClient.post('/orders', data)
  },
  update(id, data) {
    return apiClient.put(`/orders/${id}`, data)
  },
  complete(id) {
    return apiClient.post(`/orders/${id}/complete`)
  },
  cancel(id) {
    return apiClient.post(`/orders/${id}/cancel`)
  },
  reopen(id) {
    return apiClient.post(`/orders/${id}/reopen`)
  },
  attachTask(id, taskId) {
    return apiClient.post(`/orders/${id}/tasks`, { taskId })
  },
  detachTask(id, taskId) {
    return apiClient.delete(`/orders/${id}/tasks/${taskId}`)
  },
  // привязка/смена заказа из формы задания (UC-6)
  setTaskOrder(taskId, orderId) {
    return apiClient.post(`/tasks/${taskId}/set-order`, { orderId })
  }
}
