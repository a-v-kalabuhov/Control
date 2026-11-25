import apiClient from './client'

export const shiftsApi = {
  getShifts() {
    return apiClient.get('/shifts')
  },
  saveShifts(shifts) {
    return apiClient.put('/shifts', { shifts })
  },
  getDefaultLeads() {
    return apiClient.get('/shifts/default-leads')
  },
  saveDefaultLeads(items) {
    return apiClient.put('/shifts/default-leads', { items })
  }
}
