import apiClient from './client'

export const tasksApi = {
  // Получить список заданий с фильтрацией
  getList(params) {
    return apiClient.get('/tasks', { params })
  },

  // Получить детали задания
  getById(id) {
    return apiClient.get(`/tasks/${id}`)
  },

  // Создать новое задание
  create(data) {
    return apiClient.post('/tasks', data)
  },

  // Обновить задание
  update(id, data) {
    return apiClient.put(`/tasks/${id}`, data)
  },

  // Начать выполнение (сканирование QR)
  start(id, data) {
    return apiClient.post(`/tasks/${id}/start`, data)
  },

  // Завершить задание
  complete(id, data) {
    return apiClient.post(`/tasks/${id}/complete`, data)
  },

  // Закрыть задание (ручное закрытие)
  close(id, data) {
    return apiClient.post(`/tasks/${id}/close`, data)
  },

  // Удалить задание (отмена)
  delete(id) {
    return apiClient.delete(`/tasks/${id}`)
  }
}