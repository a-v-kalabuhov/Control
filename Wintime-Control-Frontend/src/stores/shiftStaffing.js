import { defineStore, getActivePinia } from 'pinia'
import { ElMessage } from 'element-plus'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { apiErrorMessage } from '@/utils/apiError'
import { registerAuthScopedReset } from '@/stores/authScopedStores'

function invalidateMutation(store) {
  store.mutationSeq += 1
  store.saving = false
}

function clearInaccessibleItem(store, itemId = null) {
  store.loadSeq += 1
  invalidateMutation(store)

  const currentId = store.currentItem?.shift?.itemId
  if (!itemId || currentId === itemId) store.currentItem = null

  const nearestId = store.nearest?.itemId ?? store.nearestContextItemId
  if (!itemId || nearestId === itemId) {
    store.nearest = null
    store.nearestContextItemId = null
  }
  store.loading = false
  store.refreshing = false
}

function isAccessRevoked(error) {
  return [403, 404].includes(error?.response?.status)
}

async function runMutation(store, itemId, request, fallback) {
  if (store.saving) return { success: false, pending: true }
  if ((store.activeItemId && store.activeItemId !== itemId) ||
    store.currentItem?.shift?.itemId !== itemId) {
    return { success: false, stale: true }
  }
  if (store.viewKind !== 'item' || store.activeItemId !== itemId) {
    store.activateItem(itemId)
  }

  const generation = ++store.mutationSeq
  const isCurrent = () => store.mutationSeq === generation &&
    store.viewKind === 'item' && store.activeItemId === itemId &&
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
    viewKind: 'nearest',
    activeItemId: null,
    nearestContextItemId: null,
    loadSeq: 0,
    historySeq: 0,
    mutationSeq: 0,
    operatorSeq: 0,
    ownerUserId: null
  }),

  getters: {
    canEditOperators: state => state.currentItem?.shift?.canEditOperators === true
  },

  actions: {
    resetForAuthChange(userId = null) {
      // Поколения не сбрасываем в ноль: поздний ответ прежнего аккаунта
      // не должен совпасть по sequence с первым запросом следующего.
      this.loadSeq += 1
      this.historySeq += 1
      this.operatorSeq += 1
      invalidateMutation(this)
      this.nearest = null
      this.currentItem = null
      this.history = []
      this.operators = []
      this.loading = false
      this.refreshing = false
      this.historyLoading = false
      this.viewKind = 'nearest'
      this.activeItemId = null
      this.nearestContextItemId = null
      this.ownerUserId = userId
    },

    activateNearest() {
      this.loadSeq += 1
      invalidateMutation(this)
      this.viewKind = 'nearest'
      this.activeItemId = null

      const currentId = this.currentItem?.shift?.itemId
      const nearestContextId = this.nearest?.itemId ?? this.nearestContextItemId
      if (currentId && currentId !== nearestContextId) {
        this.nearest = null
        this.currentItem = null
        this.nearestContextItemId = null
      } else if (nearestContextId) {
        this.nearestContextItemId = nearestContextId
      }
    },

    activateItem(itemId) {
      const id = String(itemId)
      this.loadSeq += 1
      invalidateMutation(this)
      this.viewKind = 'item'
      this.activeItemId = id

      const nearestContextId = this.nearest?.itemId ?? this.nearestContextItemId
      if (nearestContextId !== id) {
        this.nearest = null
        this.nearestContextItemId = null
      } else {
        this.nearestContextItemId = id
      }
      if (this.currentItem?.shift?.itemId !== id) this.currentItem = null
    },

    async loadNearest() {
      if (this.viewKind !== 'nearest') this.activateNearest()
      const seq = ++this.loadSeq
      const isCurrent = () => seq === this.loadSeq && this.viewKind === 'nearest'
      const background = this.currentItem !== null
      this.loading = !background
      this.refreshing = background
      let nextNearest
      let detailStarted = false

      try {
        const response = await shiftStaffingApi.nearest()
        if (!isCurrent()) return { success: false, stale: true }

        nextNearest = response.data.item
        const itemId = nextNearest?.itemId ?? this.nearest?.itemId ?? this.nearestContextItemId
        if (!itemId) {
          this.nearest = null
          this.currentItem = null
          this.nearestContextItemId = null
          return { success: true }
        }

        detailStarted = true
        const itemResponse = await shiftStaffingApi.item(itemId)
        if (!isCurrent()) return { success: false, stale: true }

        const nextItem = itemResponse.data
        if (this.currentItem?.shift?.itemId !== nextItem.shift.itemId) {
          invalidateMutation(this)
        }

        // Summary и detail меняются одной синхронной парой: таймеры и UI никогда
        // не видят nearest B вместе с currentItem A.
        this.nearest = nextNearest
        this.currentItem = nextItem
        this.nearestContextItemId = nextItem.shift.itemId
        return { success: true, data: nextItem }
      } catch (error) {
        if (!isCurrent()) return { success: false, stale: true }

        // Если nearest исчез, старый editable detail нельзя оставлять после
        // неудачного reload завершившейся смены: fail closed в пустое состояние.
        // 403/404 — авторитетный отзыв доступа независимо от наличия nearest.
        if (isAccessRevoked(error) || (detailStarted && !nextNearest)) {
          clearInaccessibleItem(this)
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
      const id = String(itemId)
      if (this.viewKind !== 'item' || this.activeItemId !== id) this.activateItem(id)
      const seq = ++this.loadSeq
      const isCurrent = () => seq === this.loadSeq &&
        this.viewKind === 'item' && this.activeItemId === id
      const background = this.currentItem?.shift?.itemId === id

      if (!background) {
        invalidateMutation(this)
        this.nearest = null
        this.currentItem = null
        this.nearestContextItemId = null
      }
      this.loading = !background
      this.refreshing = background

      try {
        const response = await shiftStaffingApi.item(itemId)
        if (!isCurrent()) return { success: false, stale: true }
        this.currentItem = response.data
        return { success: true, data: response.data }
      } catch (error) {
        if (!isCurrent()) return { success: false, stale: true }
        if (isAccessRevoked(error)) clearInaccessibleItem(this, id)
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
      const seq = ++this.operatorSeq
      try {
        const response = await shiftStaffingApi.operators()
        if (seq !== this.operatorSeq) return { success: false, stale: true }
        this.operators = response.data
        return { success: true, data: response.data }
      } catch (error) {
        if (seq !== this.operatorSeq) return { success: false, stale: true }
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

registerAuthScopedReset((userId, force) => {
  const pinia = getActivePinia()
  if (!pinia) return
  const store = useShiftStaffingStore(pinia)
  if (force || store.ownerUserId !== userId) store.resetForAuthChange(userId)
})
