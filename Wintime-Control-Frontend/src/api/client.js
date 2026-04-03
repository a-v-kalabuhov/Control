import axios from 'axios'
import { useAuthStore } from '@/stores/auth'

// Создаём экземпляр axios
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5007/api',
  timeout: parseInt(import.meta.env.VITE_API_TIMEOUT) || 30000,
  headers: {
    'Content-Type': 'application/json'
  }
})

// Интерсептор запроса - добавляем JWT токен
apiClient.interceptors.request.use(
  (config) => {
    const authStore = useAuthStore()
    const token = authStore.accessToken
    
    if (token) {
      config.headers.Authorization = `Bearer ${token}`
    }
    
    if (import.meta.env.VITE_DEBUG === 'true') {
      console.log('📤 API Request:', config.method.toUpperCase(), config.url)
    }
    
    return config
  },
  (error) => {
    console.error('❌ Request error:', error)
    return Promise.reject(error)
  }
)

// Интерсептор ответа - обработка ошибок
apiClient.interceptors.response.use(
  (response) => {
    if (import.meta.env.VITE_DEBUG === 'true') {
      console.log('📥 API Response:', response.status, response.config.url)
    }
    return response
  },
  (error) => {
    if (import.meta.env.VITE_DEBUG === 'true') {
      console.error('❌ API Error:', error.response?.status, error.response?.data)
    }
    
    // Обработка 401 - токен истёк
    if (error.response?.status === 401) {
      const authStore = useAuthStore()
      authStore.logout()
      // Можно добавить редирект на логин
    }
    
    // Обработка 403 - нет прав
    if (error.response?.status === 403) {
      // Можно показать уведомление
    }
    
    return Promise.reject(error)
  }
)

export default apiClient