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

  // Начать наладку (Issued → Setup)
  startSetup(id) {
    return apiClient.post(`/tasks/${id}/start`)
  },

  // Завершить наладку (Setup → InProgress)
  completeSetup(id) {
    return apiClient.post(`/tasks/${id}/complete-setup`)
  },

  // Отменить наладку (Setup → Issued)
  cancelSetup(id) {
    return apiClient.post(`/tasks/${id}/cancel-setup`)
  },

  // Верифицировать пресс-форму по QR
  verifyMold(id) {
    return apiClient.post(`/tasks/${id}/verify-mold`)
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
    return apiClient.post('/downtime/events/downtime/start', data)
  },

  // Завершить простой
  stopDowntime(data) {
    return apiClient.post('/downtime/events/downtime/stop', data)
  },

  // Получить причины простоев
  getDowntimeReasons(params) {
    return apiClient.get('/downtime/reasons', { params })
  },

  // Проверить QR-код (валидация)
  validateQr(code) {
    return apiClient.post('/mobile/validate-qr', { code })
  }
}