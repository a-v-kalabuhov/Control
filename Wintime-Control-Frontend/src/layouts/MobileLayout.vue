<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Шапка -->
    <header class="h-14 bg-primary-600 flex items-center justify-between px-4 sticky top-0 z-40">
      <div class="flex items-center gap-3">
        <div class="text-lg font-bold text-white">CONTROL</div>
        <span class="text-primary-200">|</span>
        <span class="text-sm text-white">Планшет наладчика</span>
      </div>

      <el-button
        type="danger"
        size="small"
        @click="handleLogout"
      >
        Выйти
      </el-button>
    </header>

    <!-- Навигация (табы) -->
    <nav class="bg-white shadow-sm sticky top-14 z-30">
      <div class="flex">
        <router-link
          to="/mobile/tasks"
          class="flex-1 py-3 text-center text-sm font-medium transition-colors"
          :class="route.path === '/mobile/tasks' 
            ? 'text-primary-600 border-b-2 border-primary-600' 
            : 'text-gray-500 hover:text-gray-700'"
        >
          <el-icon class="inline-block mr-1"><Document /></el-icon>
          Задания
        </router-link>
        <router-link
          to="/mobile/scanner"
          class="flex-1 py-3 text-center text-sm font-medium transition-colors"
          :class="route.path === '/mobile/scanner' 
            ? 'text-primary-600 border-b-2 border-primary-600' 
            : 'text-gray-500 hover:text-gray-700'"
        >
          <el-icon class="inline-block mr-1"><Scan /></el-icon>
          Сканер
        </router-link>
      </div>
    </nav>

    <!-- Контент -->
    <main class="p-4">
      <router-view />
    </main>
  </div>
</template>

<script setup>
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const authStore = useAuthStore()

const handleLogout = async () => {
  await authStore.logout()
}
</script>

<style scoped>
/* Крупные кнопки для работы в перчатках */
:deep(.el-button) {
  @apply min-h-[44px] min-w-[44px];
}

:deep(.el-input__wrapper) {
  @apply min-h-[44px];
}
</style>