// Независимый посредник разрывает цикл auth → shiftStaffing → api client → auth.
// Модуль не импортирует ни Pinia stores, ни API: подписчики вызываются синхронно,
// поэтому защищённые данные очищаются ещё до смены маршрута/пользователя.
const resetListeners = new Set()

export function registerAuthScopedReset(listener) {
  resetListeners.add(listener)
  return () => resetListeners.delete(listener)
}

export function resetAuthScopedStores(userId, force = false) {
  resetListeners.forEach(listener => listener(userId, force))
}
