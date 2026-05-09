import axios from 'axios'
import { useAuthStore } from '@/stores/auth'

// Создаём экземпляр axios
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
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
      console.log('API Request:', config.method.toUpperCase(), config.url)
    }
    
    return config
  },
  (error) => {
    console.error('Request error:', error)
    return Promise.reject(error)
  }
)

// Флаг чтобы не запускать несколько refresh одновременно
let isRefreshing = false
let refreshSubscribers = []

function onRefreshed(newToken) {
  refreshSubscribers.forEach(({ resolve }) => resolve(newToken))
  refreshSubscribers = []
}

function onRefreshFailed(error) {
  refreshSubscribers.forEach(({ reject }) => reject(error))
  refreshSubscribers = []
}

function addRefreshSubscriber(resolve, reject) {
  refreshSubscribers.push({ resolve, reject })
}

// Интерсептор ответа - обработка ошибок и автообновление токена
apiClient.interceptors.response.use(
  (response) => {
    if (import.meta.env.VITE_DEBUG === 'true') {
      console.log('API Response:', response.status, response.config.url)
    }
    return response
  },
  async (error) => {
    if (import.meta.env.VITE_DEBUG === 'true') {
      console.error('API Error:', error.response?.status, error.response?.data)
    }

    const originalRequest = error.config

    // Обработка 401 - токен истёк
    if (error.response?.status === 401) {
      const url = originalRequest?.url || ''

      // Не пытаемся рефрешить если сам refresh упал или это логин
      if (url.includes('/auth/refresh') || url.includes('/auth/login')) {
        const authStore = useAuthStore()
        authStore.clearAuth()
        return Promise.reject(error)
      }

      // Если это /auth/me при инициализации — просто возвращаем ошибку,
      // App.vue сам решит что делать
      if (url.includes('/auth/me')) {
        return Promise.reject(error)
      }

      const authStore = useAuthStore()

      if (!isRefreshing) {
        isRefreshing = true

        try {
          const response = await axios.post(
            `${import.meta.env.VITE_API_BASE_URL || '/api'}/auth/refresh`,
            {
              accessToken: authStore.accessToken,
              refreshToken: authStore.refreshToken
            }
          )

          const { accessToken, refreshToken } = response.data
          authStore.accessToken = accessToken
          authStore.refreshToken = refreshToken
          localStorage.setItem('access_token', accessToken)
          localStorage.setItem('refresh_token', refreshToken)

          isRefreshing = false
          onRefreshed(accessToken)

          // Повторяем оригинальный запрос с новым токеном
          originalRequest.headers.Authorization = `Bearer ${accessToken}`
          return apiClient(originalRequest)
        } catch (refreshError) {
          isRefreshing = false

          const isAuthRequest = url.includes('/auth/')
          if (isAuthRequest) {
            authStore.clearAuth()
          }

          const authError = new Error('Токен аутентификации истёк и не может быть обновлен')
          authError.isAuthError = true
          authError.status = 401
          authError.originalError = refreshError

          // Отклоняем все ожидающие запросы — иначе они зависнут навсегда
          onRefreshFailed(authError)
          return Promise.reject(authError)
        }
      }

      // Если refresh уже идёт — ждём его завершения и повторяем запрос
      return new Promise((resolve, reject) => {
        addRefreshSubscriber(
          (newToken) => {
            originalRequest.headers.Authorization = `Bearer ${newToken}`
            resolve(apiClient(originalRequest))
          },
          (error) => reject(error)
        )
      })
    }

    // Обработка 403 - нет прав
    if (error.response?.status === 403) {
      // Можно показать уведомление
    }

    return Promise.reject(error)
  }
)

export default apiClient
