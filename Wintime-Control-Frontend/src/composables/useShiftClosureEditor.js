import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import * as shiftClosuresApi from '@/api/shiftClosures'
import { apiErrorMessage } from '@/utils/apiError'

const MANUAL_FIELDS = [
  'actualPartWeightGrams',
  'manualFullCycleSeconds',
  'actualPackagingUnit',
  'packagingPlaces',
  'manualGoodQuantity',
  'manualDefectQuantity'
]

const FIELD_ERROR_MESSAGES = {
  required: 'Поле обязательно для заполнения',
  mustBePositive: 'Значение должно быть больше нуля',
  mustBeNonNegative: 'Значение не может быть отрицательным',
  maxTwoDecimalPlaces: 'Не больше двух знаков после запятой'
}

function manualValues(entry) {
  return Object.fromEntries(MANUAL_FIELDS.map(field => [field, entry?.[field] ?? null]))
}

export function useShiftClosureEditor(itemId) {
  const serverReport = ref(null)
  const localManualByTask = reactive({})
  const fieldErrors = ref({})
  const loading = ref(false)
  const saving = ref(false)
  let loadSequence = 0

  const report = computed(() => {
    if (!serverReport.value) return null
    return {
      ...serverReport.value,
      entries: (serverReport.value.entries ?? []).map(entry => ({
        ...entry,
        ...(localManualByTask[entry.taskId] ?? {})
      }))
    }
  })
  const isDirty = computed(() => Object.keys(localManualByTask).length > 0)

  function applyServerReport(nextReport, preserveDirty) {
    serverReport.value = nextReport
    if (!preserveDirty) {
      Object.keys(localManualByTask).forEach(taskId => delete localManualByTask[taskId])
      return
    }
    const serverEntries = new Map((nextReport?.entries ?? []).map(entry => [entry.taskId, entry]))
    for (const [taskId, local] of Object.entries(localManualByTask)) {
      const serverEntry = serverEntries.get(taskId)
      if (!serverEntry) {
        delete localManualByTask[taskId]
        continue
      }
      for (const field of Object.keys(local)) {
        if ((local[field] ?? null) === (serverEntry[field] ?? null)) delete local[field]
      }
      if (Object.keys(local).length === 0) delete localManualByTask[taskId]
    }
  }

  async function load({ preserveDirty = false } = {}) {
    if (isDirty.value && !preserveDirty) return false
    const sequence = ++loadSequence
    loading.value = true
    try {
      const response = await shiftClosuresApi.getByItem(itemId)
      if (sequence !== loadSequence) return false
      applyServerReport(response.data, preserveDirty || isDirty.value)
      fieldErrors.value = {}
      return true
    } catch (error) {
      if (sequence === loadSequence) {
        ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить сменный рапорт'))
      }
      return false
    } finally {
      if (sequence === loadSequence) loading.value = false
    }
  }

  function updateManual(taskId, field, value) {
    if (!MANUAL_FIELDS.includes(field) || report.value?.state === 'Closed') return
    const current = report.value?.entries?.find(entry => entry.taskId === taskId)
    const original = serverReport.value?.entries?.find(entry => entry.taskId === taskId)
    if (!current || !original) return
    const normalized = value ?? null
    const next = { ...(localManualByTask[taskId] ?? {}) }
    if (normalized === (original[field] ?? null)) delete next[field]
    else next[field] = normalized
    if (Object.keys(next).length === 0) delete localManualByTask[taskId]
    else localManualByTask[taskId] = next
    if (fieldErrors.value[`${taskId}:${field}`]) {
      const nextErrors = { ...fieldErrors.value }
      delete nextErrors[`${taskId}:${field}`]
      fieldErrors.value = nextErrors
    }
  }

  async function handleError(error, fallback = 'Не удалось сохранить сменный рапорт') {
    if (error?.response?.status === 409) {
      ElMessage.error(apiErrorMessage(error, 'Сменный рапорт изменён другим пользователем'))
      await load({ preserveDirty: true })
      return
    }
    const errors = error?.response?.data?.errors
    if (Array.isArray(errors) && errors.length > 0) {
      fieldErrors.value = Object.fromEntries(errors.map(error => [
        `${error.taskId}:${error.field}`,
        FIELD_ERROR_MESSAGES[error.code] ?? error.code ?? 'Некорректное значение'
      ]))
      return
    }
    ElMessage.error(apiErrorMessage(error, fallback))
  }

  async function save() {
    if (saving.value || report.value?.state === 'Closed' || !report.value) return false
    saving.value = true
    fieldErrors.value = {}
    try {
      const entries = report.value.entries.map(entry => ({
        shiftTaskId: entry.taskId,
        ...manualValues(entry)
      }))
      const response = await shiftClosuresApi.saveDraft(itemId, entries)
      // Ответ PUT уже содержит сохранённые manual values. Новые правки, сделанные
      // во время запроса, останутся локальными и не будут затёрты перезагрузкой.
      applyServerReport(response.data, true)
      return await load({ preserveDirty: true })
    } catch (error) {
      await handleError(error)
      return false
    } finally {
      saving.value = false
    }
  }

  async function close({ confirmEarlyClose = false } = {}) {
    if (saving.value || report.value?.state === 'Closed' || !report.value) return { success: false }
    if (isDirty.value) {
      const saved = await save()
      if (!saved || isDirty.value) return { success: false }
    }
    saving.value = true
    try {
      const response = await shiftClosuresApi.close(itemId, confirmEarlyClose)
      applyServerReport(response.data, false)
      fieldErrors.value = {}
      return { success: true }
    } catch (error) {
      if (error?.response?.data?.code === 'earlyCloseConfirmationRequired') {
        return { earlyCloseConfirmationRequired: true }
      }
      await handleError(error, 'Не удалось закрыть сменный рапорт')
      return { success: false }
    } finally {
      saving.value = false
    }
  }

  return {
    report,
    loading,
    saving,
    fieldErrors,
    isDirty,
    load,
    updateManual,
    save,
    close
  }
}
