import { defineStore } from 'pinia'
import { authApi } from '@/api/auth'
import router from '@/router'
import { useModulesStore } from '@/stores/modules'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: null,
    accessToken: localStorage.getItem('access_token') || null,
    refreshToken: localStorage.getItem('refresh_token') || null,
    isAuthenticated: !!localStorage.getItem('access_token'),
    userRole: localStorage.getItem('user_role') || null
  }),

  getters: {
    isAdmin: (state) => (state.user?.role ?? state.userRole) === 'Admin',
    isManager: (state) => (state.user?.role ?? state.userRole) === 'Manager',
    isAdjuster: (state) => (state.user?.role ?? state.userRole) === 'Adjuster',
    isObserver: (state) => (state.user?.role ?? state.userRole) === 'Observer',
    hasRole: (state) => (roles) => roles.includes(state.user?.role ?? state.userRole)
  },
  
  actions: {
    async login(credentials) {
      try {
        const response = await authApi.login(credentials)
        const { accessToken, refreshToken, user } = response.data
        
        this.accessToken = accessToken
        this.refreshToken = refreshToken
        this.user = user
        this.userRole = user.role
        this.isAuthenticated = true

        localStorage.setItem('access_token', accessToken)
        localStorage.setItem('refresh_token', refreshToken)
        localStorage.setItem('user_role', user.role)

        // Загружаем список модулей и обновляем реестр меню
        await useModulesStore().loadModules()

        return { success: true }
      } catch (error) {
        return { 
          success: false, 
          message: error.response?.data?.message || 'Ошибка входа' 
        }
      }
    },
    
    async logout() {
      try {
        await authApi.logout()
      } catch (error) {
        console.error('Logout error:', error)
      } finally {
        this.clearAuth()
      }
    },
    
    async refreshTokens() {
      try {
        const response = await authApi.refreshToken({
          accessToken: this.accessToken,
          refreshToken: this.refreshToken
        })
        const { accessToken, refreshToken, user } = response.data
        this.accessToken = accessToken
        this.refreshToken = refreshToken
        this.user = user
        this.userRole = user.role
        this.isAuthenticated = true
        localStorage.setItem('access_token', accessToken)
        localStorage.setItem('refresh_token', refreshToken)
        localStorage.setItem('user_role', user.role)
        return { success: true }
      } catch (error) {
        return { success: false, status: error.response?.status || null }
      }
    },

    async fetchCurrentUser() {
      try {
        const response = await authApi.getCurrentUser()
        this.user = response.data
        this.userRole = response.data.role
        localStorage.setItem('user_role', response.data.role)
        return { success: true }
      } catch (error) {
        // Возвращаем HTTP-статус ошибки, чтобы вызывающий код мог различить
        // истёкший токен (401) от сетевой ошибки или ошибки сервера (500, etc.)
        const status = error.response?.status || null
        return { success: false, status }
      }
    },
    
    clearAuth() {
      this.user = null
      this.userRole = null
      this.accessToken = null
      this.refreshToken = null
      this.isAuthenticated = false

      localStorage.removeItem('access_token')
      localStorage.removeItem('refresh_token')
      localStorage.removeItem('user_role')

      router.push('/login')
    },

    // Мягкая очистка аутентификации без перенаправления
    softClearAuth() {
      this.user = null
      this.userRole = null
      this.accessToken = null
      this.refreshToken = null
      this.isAuthenticated = false

      localStorage.removeItem('access_token')
      localStorage.removeItem('refresh_token')
      localStorage.removeItem('user_role')
    }
  }
})
