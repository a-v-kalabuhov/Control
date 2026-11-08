import { defineStore } from 'pinia'
import { ElMessage } from 'element-plus'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { apiErrorMessage } from '@/utils/apiError'

function invalidateMutation(store) {
  store.mutationSeq += 1
  store.saving = false
}

async function runMutation(store, itemId, request, fallback) {
  if (store.saving) return { success: false, pending: true }

  const generation = ++store.mutationSeq
  const isCurrent = () => store.mutationSeq === generation &&
    store.currentItem?.shift?.itemId === itemId
  store.saving = true

  try {
    const response = await request()
    if (!isCurrent()) return { success: false, stale: true }
    await store.loadItem(itemId)
    if (!isCurrent()) return { success: false, stale: true }
    return { success: true, data: response.data }
  } catch (error) {
    if (!isCurrent()) return { success: false, stale: true }

    const message = apiErrorMessage(error, fallback)
    ElMessage.error(message)

    if (error?.response?.status === 403 && store.currentItem?.shift) {
      store.currentItem.shift.canEditOperators = false
    }
    if ([403, 409].includes(error?.response?.status)) {
      await store.loadItem(itemId)
    }

    return { success: false, message }
  } finally {
    if (store.mutationSeq === generation) store.saving = false
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
    historyLoading: false,
    loadSeq: 0,
    historySeq: 0,
    mutationSeq: 0
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
      let nextNearest
      let detailStarted = false

      try {
        const response = await shiftStaffingApi.nearest()
        if (seq !== this.loadSeq) return { success: false, stale: true }

        nextNearest = response.data.item
        const itemId = nextNearest?.itemId ?? this.currentItem?.shift?.itemId
        if (!itemId) {
          this.nearest = null
          this.currentItem = null
          return { success: true }
        }

        detailStarted = true
        const itemResponse = await shiftStaffingApi.item(itemId)
        if (seq !== this.loadSeq) return { success: false, stale: true }

        const nextItem = itemResponse.data
        if (this.currentItem?.shift?.itemId !== nextItem.shift.itemId) {
          invalidateMutation(this)
        }

        // Summary и detail меняются одной синхронной парой: таймеры и UI никогда
        // не видят nearest B вместе с currentItem A.
        this.nearest = nextNearest
        this.currentItem = nextItem
        return { success: true, data: nextItem }
      } catch (error) {
        if (seq !== this.loadSeq) return { success: false, stale: true }

        // Если nearest исчез, старый editable detail нельзя оставлять после
        // неудачного reload завершившейся смены: fail closed в пустое состояние.
        if (detailStarted && !nextNearest) {
          invalidateMutation(this)
          this.nearest = null
          this.currentItem = null
        }

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
      const background = this.currentItem?.shift?.itemId === itemId

      if (!background) {
        invalidateMutation(this)
        this.nearest = null
        this.currentItem = null
      }
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
      const seq = ++this.historySeq
      this.historyLoading = true
      try {
        const response = await shiftStaffingApi.history()
        if (seq !== this.historySeq) return { success: false, stale: true }
        this.history = response.data
        return { success: true, data: response.data }
      } catch (error) {
        if (seq !== this.historySeq) return { success: false, stale: true }
        const message = apiErrorMessage(error, 'Не удалось загрузить историю смен')
        ElMessage.error(message)
        return { success: false, message }
      } finally {
        if (seq === this.historySeq) this.historyLoading = false
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
