import apiClient from './client'

// Настройки приложения (ADR-0015): планирование и безопасность.
export const settingsApi = {
  getPlanningDefaults() {
    return apiClient.get('/settings/planning-defaults')
  },

  updatePlanningDefaults(data) {
    return apiClient.put('/settings/planning-defaults', data)
  },

  getSecuritySettings() {
    return apiClient.get('/settings/security')
  },

  updateSecuritySettings(data) {
    return apiClient.put('/settings/security', data)
  }
}
