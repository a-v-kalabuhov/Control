import axios from 'axios'

const api = axios.create({
  baseURL: '/api', // Относительный путь для продакшена
  timeout: parseInt(import.meta.env.VITE_API_TIMEOUT) || 5000,
  headers: { 'Content-Type': 'application/json' }
})

// Глобальная обработка ошибок
api.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 404) {
      console.warn('Ресурс не найден:', error.config.url)
    }
    return Promise.reject(error)
  }
)

export const emulatorApi = {
  // Эмуляции
  getInstances: () => api.get('/emulator/instances'),
  startEmulation: (data) => api.post('/emulator/instances', data),
  stopEmulation: (immId) => api.delete(`/emulator/instances/${immId}`),  
  // Основное API
  getImms: () => api.get('/main/imm'),
  getTemplate: (id) => api.get(`/main/templates/${id}`),
  // Пресеты
  getPreset: (immId) => api.get(`/presets/${immId}`),
  savePreset: (immId, data) => api.post(`/presets/${immId}`, data),
  deletePreset: (immId) => api.delete(`/presets/${immId}`),
  listPresets: () => api.get('/presets/list')
}

export default api