import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const routes = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/auth/LoginView.vue'),
    meta: { requiresAuth: false, title: 'Вход' }
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
          title: 'Задания'
        }
      },
      {
        path: 'downtimes',
        name: 'DowntimeLog',
        component: () => import('@/views/downtimes/DowntimeLogView.vue'),
        meta: {
          roles: ['Admin', 'Manager'],
          title: 'Журнал простоев'
        }
      },
      {
        path: 'reports',
        name: 'Reports',
        component: () => import('@/views/reports/ReportsView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Отчёты' }
      },
      {
        path: 'reports/daily',
        name: 'DailyReport',
        component: () => import('@/views/reports/DailyReportView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Картина рабочего дня' }
      },
      {
        path: 'reports/equipment',
        name: 'EquipmentReport',
        component: () => import('@/views/reports/EquipmentReportView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Производительность оборудования' }
      },
      {
        path: 'reports/assets',
        name: 'AssetsReport',
        component: () => import('@/views/reports/AssetsReportView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Активы цеха' }
      },
      {
        path: 'dictionary',
        name: 'Dictionary',
        children: [
          {
            path: 'imm',
            name: 'DictionaryImm',
            component: () => import('@/views/dictionary/ImmDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник ТПА' }
          },
          {
            path: 'molds',
            name: 'DictionaryMolds',
            component: () => import('@/views/dictionary/MoldDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник пресс-форм' }
          },
          {
            path: 'personnel',
            name: 'DictionaryPersonnel',
            component: () => import('@/views/dictionary/PersonnelDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник персонала' }
          },
          {
            path: 'shifts',
            name: 'DictionaryShifts',
            component: () => import('@/views/dictionary/ShiftsDictionary.vue'),
            meta: { roles: ['Admin', 'Manager', 'Observer'], title: 'Расписание смен' }
          },
          {
            path: 'downtime-reasons',
            name: 'DictionaryDowntimeReasons',
            component: () => import('@/views/dictionary/DowntimeDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'], title: 'Причины простоев' }
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
            meta: { roles: ['Admin'], title: 'Настройки' }
          },
          {
            path: 'templates',
            name: 'AdminTemplates',
            component: () => import('@/views/admin/TemplatesView.vue'),
            meta: { roles: ['Admin'], title: 'Шаблоны' }
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
        meta: { roles: ['Adjuster'], title: 'Задания' }
      },
      {
        path: 'scanner',
        name: 'MobileScanner',
        component: () => import('@/views/mobile/MobileScannerView.vue'),
        meta: { roles: ['Adjuster'], title: 'Сканер' }
      },
      {
        path: 'downtimes',
        name: 'MobileDowntimes',
        component: () => import('@/views/mobile/MobileDowntimeView.vue'),
        meta: { roles: ['Adjuster'], title: 'Простои' }
      }
    ]
},  
  {
    path: '/:pathMatch(.*)*',
    name: 'NotFound',
    component: () => import('@/views/errors/NotFoundView.vue'),
    meta: { title: 'Страница не найдена' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to, from, next) => {
  const authStore = useAuthStore()

  // Аутентифицированный пользователь не должен попадать на страницу логина
  if (to.name === 'Login' && authStore.isAuthenticated) {
    next(authStore.isAdjuster ? '/mobile/tasks' : '/')
    return
  }

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    next('/login')
    return
  }

  if (authStore.isAuthenticated) {
    const isMobileRoute = to.matched.some(r => r.meta.isMobile)

    // Наладчик не должен видеть десктопные страницы
    if (authStore.isAdjuster && !isMobileRoute) {
      next('/mobile/tasks')
      return
    }

    // Другие роли не имеют доступа к мобильному интерфейсу
    if (!authStore.isAdjuster && isMobileRoute) {
      next('/')
      return
    }
  }

  next()
})

// Заголовок вкладки браузера = название текущей страницы (то же, что в шапке).
const APP_TITLE = 'CONTROL — управление цехом ТПА'
router.afterEach((to) => {
  const pageTitle = to.meta?.title
  document.title = pageTitle ? `${pageTitle} — CONTROL` : APP_TITLE
})

export default router