<template>
  <div class="min-h-screen bg-gray-50">
    <AppHeader />
    <main class="p-6">
      <router-view />
    </main>
  </div>
</template>

<script setup>
import AppHeader from '@/components/common/AppHeader.vue'
</script>