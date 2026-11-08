<template>
  <div class="min-h-screen bg-gray-50">
    <header class="h-14 bg-primary-600 flex items-center justify-between px-4 sticky top-0 z-40 shadow-md">
      <div class="flex items-center gap-3">
        <div class="text-lg font-bold text-white">CONTROL</div>
        <span class="text-primary-200">|</span>
        <span class="text-sm text-white">Старший смены</span>
      </div>
      <el-button data-test="logout" type="danger" size="small" @click="handleLogout">
        Выйти
      </el-button>
    </header>

    <nav class="bg-white shadow-sm sticky top-14 z-30">
      <div v-if="authStore.isShiftLead" class="flex max-w-3xl mx-auto">
        <router-link
          to="/shift-lead"
          class="flex-1 py-4 text-center text-base font-medium text-gray-600 hover:text-primary-600"
          active-class="text-primary-600 border-b-2 border-primary-600 bg-primary-50"
          exact-active-class="text-primary-600 border-b-2 border-primary-600 bg-primary-50"
        >
          Моя смена
        </router-link>
        <router-link
          to="/shift-lead/history"
          class="flex-1 py-4 text-center text-base font-medium text-gray-600 hover:text-primary-600"
          active-class="text-primary-600 border-b-2 border-primary-600 bg-primary-50"
        >
          История
        </router-link>
      </div>
      <div v-else class="max-w-3xl mx-auto">
        <router-link
          to="/planning/shift-plan"
          class="block py-4 px-4 text-base font-medium text-primary-600 hover:text-primary-700"
        >
          ← Назад к плану смен
        </router-link>
      </div>
    </nav>

    <main class="w-full max-w-3xl mx-auto p-4">
      <router-view />
    </main>
  </div>
</template>

<script setup>
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

async function handleLogout() {
  await authStore.logout()
}
</script>
