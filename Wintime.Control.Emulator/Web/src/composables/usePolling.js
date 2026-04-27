import { ref, onMounted, onUnmounted } from 'vue'

export function usePolling(fetchFn, intervalMs, immediate = true) {
  const defaultInterval = parseInt(import.meta.env.VITE_API_POLLING_INTERVAL) || 3000
  const interval = intervalMs || defaultInterval
  
  const data = ref(null)
  const loading = ref(false)
  const error = ref(null)  // Структура: { code, message, details, status }
  let timer = null

  const execute = async () => {
    loading.value = true
    error.value = null
    try {
      const response = await fetchFn()
      data.value = response.data
    } catch (e) {
      // Сохраняем структурированную ошибку
      error.value = e.errorData || {
        code: 'NETWORK_ERROR',
        message: e.message || 'Ошибка сети',
        status: e.response?.status
      }
      console.error('Polling error:', e)
    } finally {
      loading.value = false
    }
  }

  const start = () => {
    if (immediate) execute()
    timer = setInterval(execute, interval)
  }

  const stop = () => {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  const refresh = () => execute()
  const clearError = () => error.value = null

  onMounted(() => start())
  onUnmounted(() => stop())

  return { data, loading, error, refresh, start, stop, clearError }
}