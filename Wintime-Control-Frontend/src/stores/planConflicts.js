import { defineStore } from 'pinia'
import { planningApi } from '@/api/planning'
import { conflictsOf } from '@/utils/planConflicts'

export const PLAN_CONFLICTS_REFRESH_MS = 60_000

// Конфликты плана (PZP-07b): один опрос на всё приложение — бейдж, баннер, карточка, список, форма.
export const usePlanConflictsStore = defineStore('planConflicts', {
  state: () => ({
    data: null,
    timer: null
  }),

  getters: {
    items: (state) => state.data?.items ?? [],
    taskCount: (state) => state.data?.taskCount ?? 0,
    planCount: (state) => state.data?.planCount ?? 0,
    deviationCount: (state) => state.data?.deviationCount ?? 0,
    forTask() {
      return (taskId) => conflictsOf(this.items, taskId)
    }
  },

  actions: {
    async load() {
      try {
        const { data } = await planningApi.getConflicts()
        this.data = data
        return true
      } catch {
        // Ошибка — остаётся последнее известное состояние; опрос продолжается, без сообщения раз в минуту
        return false
      }
    },

    start() {
      if (this.timer) return
      this.load()
      this.timer = setInterval(() => this.load(), PLAN_CONFLICTS_REFRESH_MS)
    },

    stop() {
      clearInterval(this.timer)
      this.timer = null
    }
  }
})
