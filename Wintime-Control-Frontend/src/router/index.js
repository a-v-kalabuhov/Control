import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/auth/LoginView.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/',
    component: () => import('@/layouts/DefaultLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      {
        path: '',
        name: 'Dashboard',
        component: () => import('@/views/dashboard/DashboardView.vue'),
        meta: { 
          roles: ['Admin', 'Manager', 'Observer'],
          title: 'Дашборд'
        }
      },
      {
        path: 'tasks',
        name: 'Tasks',
        component: () => import('@/views/tasks/TasksView.vue'),
        meta: { 
          roles: ['Admin', 'Manager'],
          title: 'Диспетчерская'
        }
      },
      {
        path: 'reports',
        name: 'Reports',
        component: () => import('@/views/reports/ReportsView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Отчёты' },
        children: [
          {
            path: 'daily',
            name: 'DailyReport',
            component: () => import('@/views/reports/DailyReportView.vue'),
            meta: { roles: ['Admin', 'Manager'] }
          },
          {
            path: 'equipment',
            name: 'EquipmentReport',
            component: () => import('@/views/reports/EquipmentReportView.vue'),
            meta: { roles: ['Admin', 'Manager'] }
          },
          {
            path: 'assets',
            name: 'AssetsReport',
            component: () => import('@/views/reports/AssetsReportView.vue'),
            meta: { roles: ['Admin', 'Manager'] }
          }
        ]
      },      
      {
        path: 'dictionary',
        name: 'Dictionary',
        children: [
          {
            path: 'imm',
            name: 'DictionaryImm',
            component: () => import('@/views/dictionary/ImmDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'] }
          },
          {
            path: 'molds',
            name: 'DictionaryMolds',
            component: () => import('@/views/dictionary/MoldDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'] }
          },
          {
            path: 'personnel',
            name: 'DictionaryPersonnel',
            component: () => import('@/views/dictionary/PersonnelDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'] }
          }
        ]
      },
      {
        path: 'admin',
        name: 'Admin',
        children: [
          {
            path: 'settings',
            name: 'AdminSettings',
            component: () => import('@/views/admin/SettingsView.vue'),
            meta: { roles: ['Admin'] }
          },
          {
            path: 'templates',
            name: 'AdminTemplates',
            component: () => import('@/views/admin/TemplatesView.vue'),
            meta: { roles: ['Admin'] }
          }
        ]
      }
    ]
  },
  {
    path: '/mobile',
    component: () => import('@/layouts/MobileLayout.vue'),
    meta: { requiresAuth: true, isMobile: true },
    children: [
      {
        path: 'tasks',
        name: 'MobileTasks',
        component: () => import('@/views/mobile/MobileTasksView.vue'),
        meta: { roles: ['Adjuster'] }
      },
      {
        path: 'scanner',
        name: 'MobileScanner',
        component: () => import('@/views/mobile/MobileScannerView.vue'),
        meta: { roles: ['Adjuster'] }
      }
    ]
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'NotFound',
    component: () => import('@/views/errors/NotFoundView.vue')
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

// Базовый guard
router.beforeEach((to, from, next) => {
  const authStore = useAuthStore()
  
  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    next('/login')
  } else {
    next()
  }
})

export default router