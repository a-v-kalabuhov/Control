import apiClient from './client'

export const telemetryAdminApi = {
  getStatistics(timeZone) {
    return apiClient.get('/admin/telemetry/statistics', { params: { timeZone } })
  },

  previewCleanup(date, timeZone) {
    return apiClient.get('/admin/telemetry/cleanup/preview', {
      params: { date, timeZone }
    })
  },

  cleanup(date, timeZone) {
    return apiClient.delete('/admin/telemetry/cleanup', {
      params: { date, timeZone },
      timeout: 31 * 60 * 1000
    })
  }
}
