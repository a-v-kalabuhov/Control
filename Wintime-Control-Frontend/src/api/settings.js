import apiClient from './client'

// Настройки приложения (ADR-0015). Сейчас — только умолчания планирования (PZP-14).
export const settingsApi = {
  getPlanningDefaults() {
    return apiClient.get('/settings/planning-defaults')
  },

  updatePlanningDefaults(data) {
    return apiClient.put('/settings/planning-defaults', data)
  }
}
