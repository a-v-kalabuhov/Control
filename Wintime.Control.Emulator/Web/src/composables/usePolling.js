import { ref, onMounted, onUnmounted } from 'vue'

export function usePolling(fetchFn, intervalMs = 3000, immediate = true) {
  const data = ref(null)
  const loading = ref(false)
  const error = ref(null)
  let timer = null

  const execute = async () => {
    loading.value = true
    error.value = null
    try {
      const response = await fetchFn()
      data.value = response.data
    } catch (e) {
      error.value = e.message || 'Ошибка загрузки'
      console.error('Polling error:', e)
    } finally {
      loading.value = false
    }
  }

  const start = () => {
    if (immediate) execute()
    timer = setInterval(execute, intervalMs)
  }

  const stop = () => {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  const refresh = () => execute()

  onMounted(() => start())
  onUnmounted(() => stop())

  return { data, loading, error, refresh, start, stop }
}