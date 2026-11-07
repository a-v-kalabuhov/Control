import apiClient from './client'

export const dashboardApi = {
  // Получить список всех ТПА со статусами
  getImmList(params) {
    return apiClient.get('/imm', { params })
  },

  // Получить статус конкретного ТПА
  getImmStatus(id) {
    return apiClient.get(`/imm/${id}/status`)
  },

  // Получить статистику по ТПА
  getImmStatistics(id, params) {
    return apiClient.get(`/imm/${id}/statistics`, { params })
  },

  // История статусов ТПА за период (для таймлайна смены)
  getImmStatusHistory(id, params) {
    return apiClient.get(`/imm/${id}/status-history`, { params })
  },

  // История эффективного состояния ТПА за период (для таймлайна смены)
  getImmEffectiveStatusHistory(id, params) {
    return apiClient.get(`/imm/${id}/effective-status-history`, { params })
  },

  // Получить активные задания
  getActiveTasks() {
    return apiClient.get('/tasks', {
      params: { status: 'InProgress' }
    })
  },

  // Карточка ТПА литейщика: окно смены и данные определяет сервер (PZP-30)
  getImmCardShift(id) {
    return apiClient.get(`/dashboard/imm-card/${id}`)
  },

  // Итоги задания литейщика с серверной проверкой принадлежности текущей смене
  getTaskResults(id) {
    return apiClient.get(`/dashboard/task-results/${id}`)
  },

  // Средняя загрузка цеха за период (взвешенная по времени)
  getShiftUtilization(from, to) {
    return apiClient.get('/dashboard/shift-utilization', {
      params: { from: from.toISOString(), to: to.toISOString() }
    })
  }
}