import apiClient from './client'

// Сервер ограничивает длительные запросы администрирования 30 минутами.
const TELEMETRY_ADMIN_TIMEOUT_MS = 31 * 60 * 1000

export const telemetryAdminApi = {
  getStatistics(timeZone) {
    return apiClient.get('/admin/telemetry/statistics', {
      params: { timeZone },
      timeout: TELEMETRY_ADMIN_TIMEOUT_MS
    })
  },

  previewCleanup(date, timeZone) {
    return apiClient.get('/admin/telemetry/cleanup/preview', {
      params: { date, timeZone },
      timeout: TELEMETRY_ADMIN_TIMEOUT_MS
    })
  },

  cleanup(date, timeZone) {
    return apiClient.delete('/admin/telemetry/cleanup', {
      params: { date, timeZone },
      timeout: TELEMETRY_ADMIN_TIMEOUT_MS
    })
  }
}
