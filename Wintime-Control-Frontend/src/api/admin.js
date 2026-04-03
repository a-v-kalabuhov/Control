import apiClient from './client'

export const adminApi = {
  getSettings() {
    return apiClient.get('/admin/settings')
  },
  
  updateSettings(data) {
    return apiClient.put('/admin/settings', data)
  },
  
  testMqttConnection(data) {
    return apiClient.post('/admin/settings/test-mqtt', data)
  }
}