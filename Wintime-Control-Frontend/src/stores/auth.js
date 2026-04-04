import { defineStore } from 'pinia'
import { authApi } from '@/api/auth'
import router from '@/router'

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: null,
    accessToken: localStorage.getItem('access_token') || null,
    refreshToken: localStorage.getItem('refresh_token') || null,
    isAuthenticated: !!localStorage.getItem('access_token')
  }),
  
  getters: {
    isAdmin: (state) => state.user?.role === 'Admin',
    isManager: (state) => state.user?.role === 'Manager',
    isAdjuster: (state) => state.user?.role === 'Adjuster',
    isObserver: (state) => state.user?.role === 'Observer',
    hasRole: (state) => (roles) => roles.includes(state.user?.role)
  },
  
  actions: {
    async login(credentials) {
      try {
        const response = await authApi.login(credentials)
        const { accessToken, refreshToken, user } = response.data
        
        this.accessToken = accessToken
        this.refreshToken = refreshToken
        this.user = user
        this.isAuthenticated = true
        
        localStorage.setItem('access_token', accessToken)
        localStorage.setItem('refresh_token', refreshToken)
        
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
        this.isAuthenticated = true
        localStorage.setItem('access_token', accessToken)
        localStorage.setItem('refresh_token', refreshToken)
        return { success: true }
      } catch (error) {
        return { success: false, status: error.response?.status || null }
      }
    },

    async fetchCurrentUser() {
      try {
        const response = await authApi.getCurrentUser()
        this.user = response.data
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
      this.accessToken = null
      this.refreshToken = null
      this.isAuthenticated = false
      
      localStorage.removeItem('access_token')
      localStorage.removeItem('refresh_token')
      
      router.push('/login')
    },

    // Мягкая очистка аутентификации без перенаправления
    softClearAuth() {
      this.user = null
      this.accessToken = null
      this.refreshToken = null
      this.isAuthenticated = false
      
      localStorage.removeItem('access_token')
      localStorage.removeItem('refresh_token')
    }
  }
})
