<template>
  <router-view />
</template>

<script setup>
import { onMounted } from 'vue'
import { useAuthStore } from '@/stores/auth'

const authStore = useAuthStore()

// Router и App разделяют один restore promise: role guards не принимают решения
// по необязательному cached user_role до завершения /auth/me.
onMounted(() => authStore.ensureInitialized())
</script>

<style>
#app {
  @apply min-h-screen;
}
</style>
