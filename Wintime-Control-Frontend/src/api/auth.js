import apiClient from './client'

export const authApi = {
  login(credentials) {
    return apiClient.post('/auth/login', credentials)
  },
  
  logout() {
    return apiClient.post('/auth/logout')
  },
  
  getCurrentUser() {
    return apiClient.get('/auth/me')
  },
  
  refreshToken({ accessToken, refreshToken }) {
    return apiClient.post('/auth/refresh', { accessToken, refreshToken })
  }
}