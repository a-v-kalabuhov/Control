import { defineStore } from 'pinia'
import { modulesApi } from '@/api/modules'
import { menuRegistry } from '@/modules/menuRegistry'

export const useModulesStore = defineStore('modules', {
  state: () => ({
    modules: [],
    loading: false
  }),

  getters: {
    isModuleLoaded: (state) => (key) =>
      state.modules.some(m => m.key === key && m.isLoaded),

    enabledModules: (state) =>
      state.modules.filter(m => m.isEnabled)
  },

  actions: {
    async loadModules() {
      this.loading = true
      try {
        const response = await modulesApi.getModules()
        this.modules = response.data

        // Синхронизируем реестр меню с загруженными модулями
        menuRegistry.reset()
        for (const mod of this.modules) {
          if (mod.isLoaded) {
            menuRegistry.registerModule(mod.key)
          }
        }
      } catch (error) {
        // Если /api/modules недоступен — регистрируем Imm по умолчанию,
        // чтобы интерфейс не оказался пустым
        console.warn('Failed to load modules, using defaults:', error?.message)
        menuRegistry.reset()
        menuRegistry.registerModule('Imm')
      } finally {
        this.loading = false
      }
    },

    async enableModule(key) {
      const response = await modulesApi.enableModule(key)
      const mod = this.modules.find(m => m.key === key)
      if (mod) mod.isEnabled = true
      return response.data
    },

    async disableModule(key, retainData = true) {
      const response = await modulesApi.disableModule(key, retainData)
      const mod = this.modules.find(m => m.key === key)
      if (mod) mod.isEnabled = false
      return response.data
    }
  }
})
