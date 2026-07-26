export const ORDER_STATUS = {
  Active:    { label: 'Активен',  bg: 'bg-blue-100',  text: 'text-blue-800',  dot: 'bg-blue-500',  hex: '#3b82f6' },
  Completed: { label: 'Выполнен', bg: 'bg-green-100', text: 'text-green-800', dot: 'bg-green-500', hex: '#22c55e' },
  Cancelled: { label: 'Отменён',  bg: 'bg-gray-100',  text: 'text-gray-800',  dot: 'bg-gray-500',  hex: '#9ca3af' },
}

export const ORDER_STATUS_KEYS = Object.keys(ORDER_STATUS)

export function getOrderStatusMeta(key) {
  return ORDER_STATUS[key] || ORDER_STATUS.Active
}
