import apiClient from './client'

export const modulesApi = {
  getModules() {
    return apiClient.get('/modules')
  },
  enableModule(key) {
    return apiClient.post(`/modules/${key}/enable`)
  },
  disableModule(key, retainData = true) {
    return apiClient.post(`/modules/${key}/disable`, null, { params: { retainData } })
  },
  getMaintenanceStatus() {
    return apiClient.get('/admin/maintenance/status')
  },
  enterMaintenance() {
    return apiClient.post('/admin/maintenance/enter')
  },
  exitMaintenance() {
    return apiClient.post('/admin/maintenance/exit')
  },
  applyMigrations() {
    return apiClient.post('/admin/maintenance/migrate')
  },
  restartApp() {
    return apiClient.post('/admin/maintenance/restart')
  }
}
