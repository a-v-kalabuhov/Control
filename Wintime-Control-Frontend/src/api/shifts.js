import apiClient from './client'

export const shiftsApi = {
  getShifts() {
    return apiClient.get('/shifts')
  },
  saveShifts(shifts) {
    return apiClient.put('/shifts', { shifts })
  }
}
