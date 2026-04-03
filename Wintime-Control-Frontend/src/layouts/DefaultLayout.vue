<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Боковое меню -->
    <aside 
      class="fixed left-0 top-0 h-full w-64 bg-white shadow-lg z-50 transition-transform duration-300"
      :class="sidebarCollapsed ? '-translate-x-full' : 'translate-x-0'"
    >
      <!-- Логотип -->
      <div class="h-16 flex items-center justify-center border-b border-gray-200">
        <div class="text-xl font-bold text-primary-700">CONTROL</div>
      </div>

      <!-- Меню навигации -->
      <nav class="p-4">
        <el-menu
          :default-active="activeMenu"
          class="border-none"
          background-color="transparent"
          text-color="#4b5563"
          active-text-color="#2563eb"
          router
        >
          <!-- Дашборд -->
          <el-menu-item index="/">
            <el-icon><Monitor /></el-icon>
            <span>Дашборд</span>
          </el-menu-item>

          <!-- Задания (для Manager, Admin) -->
          <el-menu-item 
            v-if="canAccess(['Admin', 'Manager'])" 
            index="/tasks"
          >
            <el-icon><Document /></el-icon>
            <span>Задания</span>
          </el-menu-item>

          <!-- Отчёты (для Manager, Admin) -->
          <el-menu-item 
            v-if="canAccess(['Admin', 'Manager'])" 
            index="/reports"
          >
            <el-icon><DataLine /></el-icon>
            <span>Отчёты</span>
          </el-menu-item>
          <!-- Справочники (для Admin, Manager) -->
          <el-sub-menu 
            v-if="canAccess(['Admin', 'Manager'])" 
            index="dictionary"
          >
            <template #title>
              <el-icon><Setting /></el-icon>
              <span>Справочники</span>
            </template>
            <el-menu-item index="/dictionary/imm">ТПА</el-menu-item>
            <el-menu-item index="/dictionary/molds">Пресс-формы</el-menu-item>
            <el-menu-item index="/dictionary/personnel">Персонал</el-menu-item>
          </el-sub-menu>
          <!-- В sidebar (DefaultLayout.vue) добавьте пункт меню: -->
          <el-sub-menu v-if="canAccess(['Admin'])" index="admin">
            <template #title>
              <el-icon><Setting /></el-icon>
              <span>Администрирование</span>
            </template>
            <el-menu-item index="/admin/settings">Настройки</el-menu-item>
            <el-menu-item index="/admin/templates">Шаблоны</el-menu-item>
          </el-sub-menu>      
        </el-menu>
      </nav>

      <!-- Информация о пользователе -->
      <div class="absolute bottom-0 left-0 right-0 p-4 border-t border-gray-200">
        <div class="flex items-center gap-3">
          <el-avatar :size="40" class="bg-primary-500">
            {{ userInitials }}
          </el-avatar>
          <div class="flex-1 min-w-0">
            <p class="text-sm font-medium text-gray-900 truncate">{{ authStore.user?.fullName }}</p>
            <p class="text-xs text-gray-500">{{ roleLabel }}</p>
          </div>
        </div>
        <el-button
          type="info"
          size="small"
          class="w-full mt-3"
          @click="handleLogout"
        >
          Выйти
        </el-button>
      </div>
    </aside>

    <!-- Основной контент -->
    <div 
      class="transition-all duration-300"
      :class="sidebarCollapsed ? 'ml-0' : 'ml-64'"
    >
      <!-- Шапка -->
      <header class="h-16 bg-white shadow-sm flex items-center justify-between px-6 sticky top-0 z-40">
        <div class="flex items-center gap-4">
          <el-button
            :icon="sidebarCollapsed ? 'Expand' : 'Fold'"
            circle
            @click="sidebarCollapsed = !sidebarCollapsed"
          />
          <h1 class="text-lg font-semibold text-gray-800">{{ pageTitle }}</h1>
        </div>

        <div class="flex items-center gap-4">
          <!-- Время -->
          <div class="text-sm text-gray-600">
            {{ currentTime }}
          </div>

          <!-- Уведомления (заглушка) -->
          <el-badge :value="3" class="item">
            <el-button :icon="Bell" circle />
          </el-badge>
        </div>
      </header>

      <!-- Контент страницы -->
      <main class="p-6">
        <router-view />
      </main>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { usePermissions } from '@/composables/usePermissions'
import dayjs from 'dayjs'

const route = useRoute()
const authStore = useAuthStore()
const { canAccess } = usePermissions()

const sidebarCollapsed = ref(false)
const currentTime = ref(dayjs().format('DD.MM.YYYY HH:mm'))

// Обновление времени
let timer = null
onMounted(() => {
  timer = setInterval(() => {
    currentTime.value = dayjs().format('DD.MM.YYYY HH:mm')
  }, 1000)
})

onUnmounted(() => {
  if (timer) clearInterval(timer)
})

const activeMenu = computed(() => route.path)

const pageTitle = computed(() => {
  const titles = {
    '/': 'Дашборд',
    '/tasks': 'Задания',
    '/reports': 'Отчёты',
    '/dictionary/imm': 'Справочник ТПА',
    '/dictionary/molds': 'Справочник пресс-форм',
    '/dictionary/personnel': 'Справочник персонала'
  }
  return titles[route.path] || 'CONTROL'
})

const userInitials = computed(() => {
  const name = authStore.user?.fullName || 'U'
  return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2)
})

const roleLabel = computed(() => {
  const labels = {
    'Admin': 'Администратор',
    'Manager': 'Руководитель',
    'Adjuster': 'Наладчик',
    'Observer': 'Наблюдатель'
  }
  return labels[authStore.user?.role] || authStore.user?.role
})

const handleLogout = async () => {
  await authStore.logout()
}
</script>

<style scoped>
:deep(.el-menu) {
  border-right: none !important;
}

:deep(.el-menu-item) {
  @apply rounded-lg mb-1;
}

:deep(.el-menu-item:hover) {
  @apply bg-primary-50;
}

:deep(.el-menu-item.is-active) {
  @apply bg-primary-100 text-primary-700;
}
</style>