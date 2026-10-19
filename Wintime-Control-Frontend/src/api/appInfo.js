import apiClient from './client'

// Публичные сведения о стенде (без авторизации): сейчас — только признак демо-режима.
export const appInfoApi = {
  get() {
    return apiClient.get('/app-info')
  }
}
