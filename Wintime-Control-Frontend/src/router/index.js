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
          roles: ['Admin', 'Manager', 'Observer', 'Operator'],
          title: 'Дашборд'
        }
      },
      {
        path: 'tasks',
        name: 'Tasks',
        component: () => import('@/views/tasks/TasksView.vue'),
        meta: {
          roles: ['Admin', 'Manager', 'Observer'],
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
        path: 'unplanned-runs',
        name: 'UnplannedRunLog',
        component: () => import('@/views/unplanned/UnplannedRunLogView.vue'),
        meta: {
          roles: ['Admin', 'Manager'],
          title: 'Журнал работы без задания'
        }
      },
      {
        path: 'orders',
        name: 'orders',
        component: () => import('@/views/OrdersView.vue'),
        meta: { roles: ['Admin', 'Manager', 'Observer'], title: 'Заказы' }
      },
      {
        // План-график цеха (PZP-07a): Гантт загрузки ТПА; Observer — только чтение
        path: 'planning/gantt',
        name: 'PlanningGantt',
        component: () => import('@/views/planning/PlanningGanttView.vue'),
        meta: { roles: ['Admin', 'Manager', 'Observer'], title: 'План-график цеха' }
      },
      {
        // PZP-18: ежедневное назначение наладчиков и выдача заданий на смену
        path: 'planning/shift-work-plan',
        name: 'ShiftWorkPlan',
        component: () => import('@/views/planning/ShiftWorkPlanView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'План работ на смену' }
      },
      {
        // PZP-32: назначение старших на позиции заводского графика
        path: 'planning/shift-plan',
        name: 'ShiftPlan',
        component: () => import('@/views/planning/ShiftPlanView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'План смен' }
      },
      {
        // Мониторинг телеметрии; ТПА — в query: ?imm=<id>[&from=card]
        path: 'monitoring',
        name: 'Monitoring',
        component: () => import('@/views/telemetry/TelemetryDashboardView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Мониторинг' }
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
            path: 'product-types',
            name: 'DictionaryProductTypes',
            component: () => import('@/views/dictionary/ProductTypeDictionary.vue'),
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник изделий' }
          },
          {
            path: 'plastics',
            name: 'DictionaryPlastics',
            component: () => import('@/views/dictionary/MaterialDictionary.vue'),
            props: { materialType: 'Plastic' },
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник пластиков' }
          },
          {
            path: 'colorants',
            name: 'DictionaryColorants',
            component: () => import('@/views/dictionary/MaterialDictionary.vue'),
            props: { materialType: 'Colorant' },
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник красителей' }
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
        path: 'settings',
        name: 'Settings',
        component: () => import('@/views/settings/SettingsView.vue'),
        meta: { roles: ['Admin', 'Manager'], title: 'Настройки' }
      },
      {
        path: 'admin',
        name: 'Admin',
        children: [
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
    path: '/shift-lead',
    component: () => import('@/layouts/ShiftLeadLayout.vue'),
    meta: { requiresAuth: true, isShiftLeadUi: true },
    children: [
      {
        path: '',
        name: 'ShiftLeadHome',
        component: () => import('@/views/shift-lead/ShiftLeadPlaceholderView.vue'),
        meta: { roles: ['ShiftLead'], title: 'Моя смена' }
      },
      {
        path: 'history',
        name: 'ShiftLeadHistory',
        component: () => import('@/views/shift-lead/ShiftLeadPlaceholderView.vue'),
        meta: { roles: ['ShiftLead'], title: 'История смен' }
      },
      {
        path: 'items/:itemId',
        name: 'ShiftStaffingItem',
        component: () => import('@/views/shift-lead/ShiftLeadPlaceholderView.vue'),
        meta: { roles: ['ShiftLead', 'Manager', 'Admin'], title: 'Смена' }
      },
      {
        path: 'items/:itemId/tasks/:taskId',
        name: 'ShiftStaffingTask',
        component: () => import('@/views/shift-lead/ShiftLeadPlaceholderView.vue'),
        meta: { roles: ['ShiftLead', 'Manager', 'Admin'], title: 'Задание' }
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

router.beforeEach(async (to) => {
  const authStore = useAuthStore()

  // При перезагрузке сначала восстанавливаем пользователя; все role decisions
  // ниже видят серверную роль, а не устаревший localStorage snapshot.
  await authStore.ensureInitialized()

  // Аутентифицированный пользователь не должен попадать на страницу логина
  if (to.name === 'Login' && authStore.isAuthenticated) {
    return authStore.homePath
  }

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    return '/login'
  }

  if (authStore.isAuthenticated) {
    const isMobileRoute = to.matched.some(r => r.meta.isMobile)
    const isShiftLeadRoute = to.matched.some(r => r.meta.isShiftLeadUi)

    // Наладчик не должен видеть десктопные страницы и контур старшего смены.
    if (authStore.isAdjuster && !isMobileRoute) {
      return authStore.homePath
    }

    // Другие роли не имеют доступа к мобильному интерфейсу наладчика.
    if (!authStore.isAdjuster && isMobileRoute) {
      return authStore.homePath
    }

    // Старший смены работает только в отдельном семействе маршрутов.
    if (authStore.isShiftLead && !isShiftLeadRoute) {
      return authStore.homePath
    }

    // Ограничения ролей действуют и при прямом переходе по URL, не только в меню.
    if (to.meta.roles && !authStore.hasRole(to.meta.roles)) {
      return authStore.homePath
    }
  }

  return true
})

// Заголовок вкладки браузера = название текущей страницы (то же, что в шапке).
const APP_TITLE = 'CONTROL — управление цехом ТПА'
router.afterEach((to) => {
  const pageTitle = to.meta?.title
  document.title = pageTitle ? `${pageTitle} — CONTROL` : APP_TITLE
})

export default router