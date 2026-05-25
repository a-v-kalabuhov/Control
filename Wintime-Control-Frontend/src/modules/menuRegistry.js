import { reactive } from 'vue'

// Платформенные пункты меню — всегда присутствуют
const platformItems = [
  {
    type: 'item',
    path: '/',
    icon: 'Monitor',
    label: 'Дашборд',
    roles: ['Admin', 'Manager', 'Observer']
  }
]

// Пункты Admin-подменю — платформенные
const platformAdminChildren = [
  { path: '/admin/settings',     label: 'Настройки' },
  { path: '/admin/templates',    label: 'Шаблоны' },
  { path: '/admin/modules',      label: 'Модули' },
  { path: '/admin/maintenance',  label: 'Обслуживание' }
]

// Меню-пункты модуля Imm.
// Когда ImmModule станет внешним плагином, это будет экспортироваться из imm-module.js.
const immMenuItems = [
  {
    type: 'item',
    path: '/tasks',
    icon: 'Document',
    label: 'Задания',
    roles: ['Admin', 'Manager'],
    moduleKey: 'Imm'
  },
  {
    type: 'item',
    path: '/reports',
    icon: 'DataLine',
    label: 'Отчёты',
    roles: ['Admin', 'Manager'],
    moduleKey: 'Imm'
  },
  {
    type: 'submenu',
    index: 'dictionary',
    icon: 'Notebook',
    label: 'Справочники',
    roles: ['Admin', 'Manager'],
    moduleKey: 'Imm',
    children: [
      { path: '/dictionary/imm',       label: 'ТПА',            roles: ['Admin', 'Manager'] },
      { path: '/dictionary/molds',     label: 'Пресс-формы',    roles: ['Admin', 'Manager'] },
      { path: '/dictionary/personnel', label: 'Персонал',       roles: ['Admin', 'Manager'] },
      { path: '/dictionary/shifts',    label: 'Смены',          roles: ['Admin', 'Manager', 'Observer'] }
    ]
  },
  // Смены отдельным пунктом для Observer (вне подменю Справочники)
  {
    type: 'item',
    path: '/dictionary/shifts',
    icon: 'Clock',
    label: 'Смены',
    roles: ['Observer'],
    moduleKey: 'Imm'
  }
]

// Map известных модулей → их пункты меню
const moduleMenuItemsMap = {
  Imm: immMenuItems
}

export const menuRegistry = reactive({
  items: [...platformItems],
  adminChildren: [...platformAdminChildren],

  // Вызывается при загрузке модуля (сейчас — из modulesStore, в будущем — из imm-module.js)
  registerModule(moduleKey) {
    const items = moduleMenuItemsMap[moduleKey]
    if (!items) return
    // Не добавлять дважды
    if (this.items.some(i => i.moduleKey === moduleKey)) return
    this.items.push(...items)
  },

  unregisterModule(moduleKey) {
    this.items = this.items.filter(i => i.moduleKey !== moduleKey)
  },

  // Полный сброс — для перезагрузки модулей
  reset() {
    this.items = [...platformItems]
  }
})
