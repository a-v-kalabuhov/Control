<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Шапка -->
    <header class="h-14 bg-primary-600 flex items-center justify-between px-4 sticky top-0 z-40 shadow-md">
      <div class="flex items-center gap-3">
        <div class="text-lg font-bold text-white">CONTROL</div>
        <span class="text-primary-200">|</span>
        <span class="text-sm text-white">Планшет наладчика</span>
      </div>

      <div class="flex items-center gap-2">
        <!-- Индикатор связи -->
        <el-tooltip content="Статус соединения">
          <div 
            class="w-3 h-3 rounded-full"
            :class="isOnline ? 'bg-green-400' : 'bg-red-400'"
          ></div>
        </el-tooltip>

        <el-button
          type="danger"
          size="small"
          @click="handleLogout"
        >
          Выйти
        </el-button>
      </div>
    </header>

    <!-- Навигация (табы) -->
    <nav class="bg-white shadow-sm sticky top-14 z-30">
      <div class="flex">
        <router-link
          to="/mobile/tasks"
          class="flex-1 py-4 text-center text-base font-medium transition-colors"
          :class="route.path === '/mobile/tasks' 
            ? 'text-primary-600 border-b-3 border-primary-600 bg-primary-50' 
            : 'text-gray-500 hover:text-gray-700'"
        >
          <el-icon class="inline-block mr-1 text-lg"><Document /></el-icon>
          Задания
        </router-link>
        <router-link
          to="/mobile/scanner"
          class="flex-1 py-4 text-center text-base font-medium transition-colors"
          :class="route.path === '/mobile/scanner' 
            ? 'text-primary-600 border-b-3 border-primary-600 bg-primary-50' 
            : 'text-gray-500 hover:text-gray-700'"
        >
          <el-icon class="inline-block mr-1 text-lg"><Scan /></el-icon>
          Сканер
        </router-link>
      </div>
    </nav>

    <!-- Контент -->
    <main class="pb-20">
      <router-view />
    </main>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const isOnline = computed(() => navigator.onLine)

const handleLogout = async () => {
  await authStore.logout()
}

// Слушатель изменения статуса сети
window.addEventListener('online', () => {
  // Можно показать уведомление о восстановлении связи
})

window.addEventListener('offline', () => {
  // Можно показать уведомление о потере связи
})
</script>

<style scoped>
.border-b-3 {
  border-bottom-width: 3px;
}

nav a {
  @apply min-h-[60px];
}
</style>