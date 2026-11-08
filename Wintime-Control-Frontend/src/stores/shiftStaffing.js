import { defineStore } from 'pinia'
import { ElMessage } from 'element-plus'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { apiErrorMessage } from '@/utils/apiError'

async function runMutation(store, itemId, request, fallback) {
  if (store.saving) return { success: false, pending: true }

  store.saving = true
  try {
    const response = await request()
    if (itemId) await store.loadItem(itemId)
    return { success: true, data: response.data }
  } catch (error) {
    const message = apiErrorMessage(error, fallback)
    ElMessage.error(message)

    if (error?.response?.status === 403 && store.currentItem?.shift) {
      store.currentItem.shift.canEditOperators = false
    }
    if (itemId && [403, 409].includes(error?.response?.status)) {
      await store.loadItem(itemId)
    }

    return { success: false, message }
  } finally {
    store.saving = false
  }
}

export const useShiftStaffingStore = defineStore('shiftStaffing', {
  state: () => ({
    nearest: null,
    currentItem: null,
    history: [],
    operators: [],
    loading: false,
    refreshing: false,
    saving: false,
    loadSeq: 0
  }),

  getters: {
    canEditOperators: state => state.currentItem?.shift?.canEditOperators === true
  },

  actions: {
    async loadNearest() {
      const seq = ++this.loadSeq
      const background = this.currentItem !== null
      this.loading = !background
      this.refreshing = background

      try {
        const response = await shiftStaffingApi.nearest()
        if (seq !== this.loadSeq) return { success: false, stale: true }

        this.nearest = response.data.item
        const itemId = this.nearest?.itemId ?? this.currentItem?.shift?.itemId
        if (!itemId) {
          this.currentItem = null
          return { success: true }
        }

        // Если ближайшей смены больше нет, перечитываем прежнюю: сервер вернёт
        // завершившуюся смену с canEditOperators=false для read-only просмотра.
        return await this.loadItem(itemId)
      } catch (error) {
        if (seq !== this.loadSeq) return { success: false, stale: true }
        const message = apiErrorMessage(error, 'Не удалось загрузить ближайшую смену')
        ElMessage.error(message)
        return { success: false, message }
      } finally {
        if (seq === this.loadSeq) {
          this.loading = false
          this.refreshing = false
        }
      }
    },

    async loadItem(itemId) {
      const seq = ++this.loadSeq
      const background = this.currentItem !== null
      this.loading = !background
      this.refreshing = background

      try {
        const response = await shiftStaffingApi.item(itemId)
        if (seq !== this.loadSeq) return { success: false, stale: true }
        this.currentItem = response.data
        return { success: true, data: response.data }
      } catch (error) {
        if (seq !== this.loadSeq) return { success: false, stale: true }
        const message = apiErrorMessage(error, 'Не удалось загрузить смену')
        ElMessage.error(message)
        return { success: false, message }
      } finally {
        if (seq === this.loadSeq) {
          this.loading = false
          this.refreshing = false
        }
      }
    },

    async loadHistory() {
      this.loading = true
      try {
        const response = await shiftStaffingApi.history()
        this.history = response.data
        return { success: true, data: response.data }
      } catch (error) {
        const message = apiErrorMessage(error, 'Не удалось загрузить историю смен')
        ElMessage.error(message)
        return { success: false, message }
      } finally {
        this.loading = false
      }
    },

    async loadOperators() {
      try {
        const response = await shiftStaffingApi.operators()
        this.operators = response.data
        return { success: true, data: response.data }
      } catch (error) {
        const message = apiErrorMessage(error, 'Не удалось загрузить список литейщиков')
        ElMessage.error(message)
        return { success: false, message }
      }
    },

    async assign(itemId, immId, operatorId) {
      return runMutation(
        this,
        itemId,
        () => shiftStaffingApi.assign(itemId, immId, operatorId),
        'Не удалось назначить литейщика'
      )
    },

    async unassign(assignmentId) {
      const itemId = this.currentItem?.shift?.itemId
      return runMutation(
        this,
        itemId,
        () => shiftStaffingApi.unassign(assignmentId),
        'Не удалось снять литейщика'
      )
    }
  }
})
