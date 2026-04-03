import { useAuthStore } from '@/stores/auth'

export function usePermissions() {
  const authStore = useAuthStore()

  /**
   * Проверка доступа по ролям
   * @param {string[]} roles - Массив разрешённых ролей
   * @returns {boolean}
   */
  function canAccess(roles) {
    if (!authStore.isAuthenticated) return false
    return roles.includes(authStore.user?.role)
  }

  /**
   * Проверка конкретной роли
   * @param {string} role - Роль для проверки
   * @returns {boolean}
   */
  function hasRole(role) {
    return authStore.user?.role === role
  }

  /**
   * Проверка на администратора
   * @returns {boolean}
   */
  function isAdmin() {
    return hasRole('Admin')
  }

  /**
   * Проверка на руководителя
   * @returns {boolean}
   */
  function isManager() {
    return hasRole('Manager') || isAdmin()
  }

  /**
   * Проверка на наладчика
   * @returns {boolean}
   */
  function isAdjuster() {
    return hasRole('Adjuster')
  }

  return {
    canAccess,
    hasRole,
    isAdmin,
    isManager,
    isAdjuster
  }
}