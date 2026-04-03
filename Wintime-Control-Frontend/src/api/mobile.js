import apiClient from './client'

export const mobileApi = {
  // Получить мои задания (для текущего наладчика)
  getMyTasks(params) {
    return apiClient.get('/tasks/my', { params })
  },

  // Получить детали задания
  getTaskById(id) {
    return apiClient.get(`/tasks/${id}`)
  },

  // Начать задание (сканирование QR)
  startTask(id, data) {
    return apiClient.post(`/tasks/${id}/start`, data)
  },

  // Завершить задание
  completeTask(id, data) {
    return apiClient.post(`/tasks/${id}/complete`, data)
  },

  // Закрыть задание
  closeTask(id, data) {
    return apiClient.post(`/tasks/${id}/close`, data)
  },

  // Начать простой
  startDowntime(data) {
    return apiClient.post('/events/downtime/start', data)
  },

  // Завершить простой
  stopDowntime(data) {
    return apiClient.post('/events/downtime/stop', data)
  },

  // Получить причины простоев
  getDowntimeReasons(params) {
    return apiClient.get('/downtime-reasons', { params })
  },

  // Проверить QR-код (валидация)
  validateQr(code) {
    return apiClient.post('/mobile/validate-qr', { code })
  }
}