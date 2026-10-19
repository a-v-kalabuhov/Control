import { defineStore } from 'pinia'
import { appInfoApi } from '@/api/appInfo'

// Сведения о стенде с сервера (GET /api/app-info). Грузятся один раз; при ошибке — не демо.
export const useAppInfoStore = defineStore('appInfo', {
  state: () => ({
    demoMode: false,
    loaded: false
  }),

  actions: {
    async load() {
      if (this.loaded) return
      try {
        const { data } = await appInfoApi.get()
        this.demoMode = data?.demoMode === true
      } catch {
        this.demoMode = false
      } finally {
        this.loaded = true
      }
    }
  }
})
