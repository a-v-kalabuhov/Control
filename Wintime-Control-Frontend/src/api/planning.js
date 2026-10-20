import apiClient from './client'

export const planningApi = {
  // План-график цеха (PZP-07a): from/to — ISO UTC, окно [from, to)
  getBoard(params) {
    return apiClient.get('/planning/board', { params })
  },

  // Конфликты плана всех незавершённых заданий (PZP-07b)
  getConflicts() {
    return apiClient.get('/planning/conflicts')
  }
}
