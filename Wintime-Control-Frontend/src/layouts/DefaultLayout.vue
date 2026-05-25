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
          <!-- Динамические пункты из реестра -->
          <template v-for="item in visibleMenuItems" :key="item.path || item.index">
            <!-- Простой пункт -->
            <el-menu-item v-if="item.type === 'item'" :index="item.path">
              <el-icon><component :is="item.icon" /></el-icon>
              <span>{{ item.label }}</span>
            </el-menu-item>

            <!-- Подменю -->
            <el-sub-menu v-else-if="item.type === 'submenu'" :index="item.index">
              <template #title>
                <el-icon><component :is="item.icon" /></el-icon>
                <span>{{ item.label }}</span>
              </template>
              <el-menu-item
                v-for="child in visibleChildren(item)"
                :key="child.path"
                :index="child.path"
              >
                {{ child.label }}
              </el-menu-item>
            </el-sub-menu>
          </template>

          <!-- Администрирование (платформенный, всегда для Admin) -->
          <el-sub-menu v-if="canAccess(['Admin'])" index="admin">
            <template #title>
              <el-icon><Setting /></el-icon>
              <span>Администрирование</span>
            </template>
            <el-menu-item
              v-for="child in menuRegistry.adminChildren"
              :key="child.path"
              :index="child.path"
            >
              {{ child.label }}
            </el-menu-item>
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
          <div class="text-sm text-gray-600">{{ currentTime }}</div>
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
import { Bell } from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'
import { usePermissions } from '@/composables/usePermissions'
import { menuRegistry } from '@/modules/menuRegistry'
import dayjs from 'dayjs'

const route = useRoute()
const authStore = useAuthStore()
const { canAccess } = usePermissions()

const sidebarCollapsed = ref(false)
const currentTime = ref(dayjs().format('DD.MM.YYYY HH:mm'))

let timer = null
onMounted(() => {
  timer = setInterval(() => {
    currentTime.value = dayjs().format('DD.MM.YYYY HH:mm')
  }, 1000)
})
onUnmounted(() => { if (timer) clearInterval(timer) })

const activeMenu = computed(() => route.path)

// Пункты, доступные текущему пользователю
const visibleMenuItems = computed(() =>
  menuRegistry.items.filter(item => canAccess(item.roles))
)

function visibleChildren(item) {
  return (item.children || []).filter(c => canAccess(c.roles))
}

// Заголовок страницы — сначала из meta маршрута, потом из реестра, потом дефолт
const pageTitle = computed(() => {
  if (route.meta?.title) return route.meta.title

  for (const item of menuRegistry.items) {
    if (item.path === route.path) return item.label
    if (item.children) {
      const child = item.children.find(c => c.path === route.path)
      if (child) return child.label
    }
  }
  for (const child of menuRegistry.adminChildren) {
    if (child.path === route.path) return child.label
  }
  return 'CONTROL'
})

const userInitials = computed(() => {
  const name = authStore.user?.fullName || 'U'
  return name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2)
})

const roleLabel = computed(() => {
  const labels = {
    Admin: 'Администратор',
    Manager: 'Руководитель',
    Adjuster: 'Наладчик',
    Observer: 'Наблюдатель'
  }
  return labels[authStore.user?.role] || authStore.user?.role
})

const handleLogout = async () => { await authStore.logout() }
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
