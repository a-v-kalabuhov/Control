import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'

export function useAuth() {
  const authStore = useAuthStore()

  const user = computed(() => authStore.user)
  const isAuthenticated = computed(() => authStore.isAuthenticated)
  const role = computed(() => authStore.user?.role)

  const isLoading = computed(() => authStore.loading)

  return {
    user,
    isAuthenticated,
    role,
    isLoading,
    login: authStore.login,
    logout: authStore.logout
  }
}