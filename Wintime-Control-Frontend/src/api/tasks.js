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

  // Итоги выполнения по окну выпуска (вкладка «Результаты выполнения»)
  getResults(id) {
    return apiClient.get(`/tasks/${id}/results`)
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

  // Выдать задание («Планируется» → «Выдано»); personnelId — назначить наладчика в той же операции
  issue(id, personnelId) {
    return apiClient.post(`/tasks/${id}/issue`, { personnelId: personnelId ?? null })
  },

  // Назначить/переназначить/снять наладчика (null — снять, только в «Планируется»)
  setPersonnel(id, personnelId) {
    return apiClient.put(`/tasks/${id}/personnel`, { personnelId: personnelId || null })
  },

  // Удалить задание (отмена)
  delete(id) {
    return apiClient.delete(`/tasks/${id}`)
  }
}