// Единый источник палитры эффективных состояний ТПА.
// Используется бейджем, карточкой, фильтром, KPI-панелью и таймлайном смены.
export const EFFECTIVE_STATUS = {
  Production: { label: 'Работа',             bg: 'bg-green-100',  text: 'text-green-800',  dot: 'bg-green-500',  border: 'border-green-500',  hex: '#22c55e' },
  Setup:      { label: 'Наладка',            bg: 'bg-yellow-100', text: 'text-yellow-800', dot: 'bg-yellow-500', border: 'border-yellow-500', hex: '#eab308' },
  Downtime:   { label: 'Простой',            bg: 'bg-red-100',    text: 'text-red-800',    dot: 'bg-red-500',    border: 'border-red-500',    hex: '#ef4444' },
  Unplanned:  { label: 'Работа без задания', bg: 'bg-purple-100', text: 'text-purple-800', dot: 'bg-purple-500', border: 'border-purple-500', hex: '#a855f7' },
  NoTask:     { label: 'Без задания',        bg: 'bg-blue-100',   text: 'text-blue-800',   dot: 'bg-blue-500',   border: 'border-blue-500',   hex: '#3b82f6' },
  Offline:    { label: 'Нет связи',          bg: 'bg-gray-100',   text: 'text-gray-800',   dot: 'bg-gray-500',   border: 'border-gray-400',   hex: '#9ca3af' },
}

export const EFFECTIVE_STATUS_KEYS = Object.keys(EFFECTIVE_STATUS)

export function getEffectiveStatusMeta(key) {
  return EFFECTIVE_STATUS[key] || EFFECTIVE_STATUS.Offline
}

// «Нет данных» — только для отчётов: время без единой записи статуса ТПА (и будущее).
// В EFFECTIVE_STATUS не входит: live-дашборд такого состояния не знает.
export const NO_DATA_STATUS = {
  label: 'Нет данных', bg: 'bg-gray-50', text: 'text-gray-500', dot: 'bg-gray-200', border: 'border-gray-200', hex: '#e5e7eb'
}

// Категории столбца отчёта «Производительность оборудования»; порядок = порядок в стеке.
export const REPORT_STATUS = { ...EFFECTIVE_STATUS, NoData: NO_DATA_STATUS }
export const REPORT_STATUS_KEYS = Object.keys(REPORT_STATUS)
