<template>
  <router-view />
</template>

<script setup>
import { onMounted } from 'vue'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

// При обновлении страницы токен есть в localStorage, но user=null (Pinia сброшена).
// Восстанавливаем данные пользователя из API, чтобы меню отображалось корректно.
onMounted(async () => {
  if (authStore.isAuthenticated && !authStore.user) {
    const result = await authStore.fetchCurrentUser()

    if (!result.success && result.status === 401) {
      // Access токен истёк — пробуем обновить через refresh token
      const refreshed = await authStore.refreshTokens()
      if (refreshed.success) {
        // Повторяем загрузку профиля с новым токеном
        const retry = await authStore.fetchCurrentUser()
        if (!retry.success) {
          authStore.clearAuth()
        }
      } else {
        // Refresh тоже не работает — разлогиниваем
        authStore.clearAuth()
      }
    }
    // При других ошибках (сеть, 500) — не разлогиниваем,
    // пользователь остаётся на странице с токеном в localStorage
  }
})
</script>

<style>
#app {
  @apply min-h-screen;
}
</style>
