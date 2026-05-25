<template>
  <router-view />
</template>

<script setup>
import { onMounted } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useModulesStore } from '@/stores/modules'

const authStore = useAuthStore()
const modulesStore = useModulesStore()

// При обновлении страницы токен есть в localStorage, но user=null (Pinia сброшена).
// Восстанавливаем данные пользователя из API, чтобы меню отображалось корректно.
onMounted(async () => {
  if (authStore.isAuthenticated && !authStore.user) {
    const result = await authStore.fetchCurrentUser()

    if (!result.success && result.status === 401) {
      // Access токен истёк — пробуем обновить через refresh token
      const refreshed = await authStore.refreshTokens()
      if (refreshed.success) {
        const retry = await authStore.fetchCurrentUser()
        if (!retry.success) {
          authStore.clearAuth()
          return
        }
      } else {
        authStore.clearAuth()
        return
      }
    } else if (!result.success) {
      // Сетевая ошибка — не разлогиниваем, но регистрируем пункты меню по умолчанию
      return
    }
  }

  // Загружаем модули и обновляем реестр меню, если пользователь авторизован
  if (authStore.isAuthenticated) {
    await modulesStore.loadModules()
  }
})
</script>

<style>
#app {
  @apply min-h-screen;
}
</style>
