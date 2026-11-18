<template>
  <div v-if="closed" class="shift-closure-corrections">
    <el-button
      v-if="canCorrect"
      type="primary"
      plain
      data-test="open-corrections"
      @click="openDialog"
    >
      Исправить показатели
    </el-button>
    <el-button v-else data-test="open-correction-history" @click="openDialog">
      История исправлений
    </el-button>

    <el-dialog
      v-if="visible"
      v-model="visible"
      :title="canCorrect ? 'Исправить показатели' : 'История исправлений'"
      width="900px"
      :teleported="false"
      :close-on-click-modal="false"
      destroy-on-close
    >
      <template v-if="canCorrect">
        <label class="mb-4 block text-sm">
          <span class="mb-1 block font-medium text-gray-700">Причина исправления</span>
          <textarea
            v-model="reason"
            data-test="correction-reason"
            rows="2"
            class="w-full rounded border border-gray-300 px-3 py-2"
            :disabled="saving"
            @input="reasonError = ''"
          />
          <span v-if="reasonError" class="mt-1 block text-xs text-red-600">{{ reasonError }}</span>
        </label>

        <section
          v-for="entry in entries"
          :key="entry.taskId"
          :data-correction-task="entry.taskId"
          class="mb-4 rounded-lg border border-gray-200 p-4"
        >
          <h3 class="mb-3 font-semibold text-gray-800">{{ entry.immName }} · {{ entry.productName }}</h3>
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <label v-for="field in manualFields" :key="field.name" class="block text-sm">
              <span class="mb-1 block text-gray-600">{{ field.label }}</span>
              <el-input-number
                :model-value="entry[field.name]"
                :data-correction-manual="`${entry.taskId}:${field.name}`"
                :disabled="saving"
                :min="0"
                :precision="field.precision"
                controls-position="right"
                class="w-full"
                @update:model-value="value => updateManual(entry.taskId, field.name, value)"
              />
              <span
                v-if="fieldErrors[`${entry.taskId}:${field.name}`]"
                :data-correction-error="`${entry.taskId}:${field.name}`"
                class="mt-1 block text-xs text-red-600"
              >
                {{ fieldErrors[`${entry.taskId}:${field.name}`] }}
              </span>
            </label>
          </div>
        </section>
      </template>

      <section v-loading="historyLoading" data-test="correction-history" class="mt-5 border-t border-gray-200 pt-4">
        <h3 class="mb-3 font-semibold text-gray-800">История исправлений</h3>
        <div v-if="history.length" class="space-y-3">
          <article v-for="correction in history" :key="correction.id" class="rounded bg-gray-50 p-3 text-sm">
            <div class="font-medium text-gray-800">{{ correction.changedByName }}</div>
            <div class="text-xs text-gray-500">{{ formatDateTime(correction.changedAt) }}</div>
            <div class="mt-1 text-gray-700">Причина: {{ correction.reason }}</div>
            <ul v-if="correction.changes?.length" class="mt-2 space-y-1">
              <li v-for="(change, index) in correction.changes" :key="`${change.taskId}:${change.field}:${index}`">
                {{ fieldLabel(change.field) }}: {{ formatValue(change.oldValue) }} → {{ formatValue(change.newValue) }}
              </li>
            </ul>
          </article>
        </div>
        <div v-else-if="!historyLoading" class="text-sm text-gray-500">Исправлений ещё не было</div>
      </section>

      <template #footer>
        <el-button :disabled="saving" @click="visible = false">Закрыть</el-button>
        <el-button
          v-if="canCorrect"
          type="primary"
          data-test="submit-correction"
          :loading="saving"
          :disabled="saving"
          @click="submit"
        >
          Сохранить исправление
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, nextTick, ref } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import * as shiftClosuresApi from '@/api/shiftClosures'
import { usePermissions } from '@/composables/usePermissions'
import { apiErrorMessage } from '@/utils/apiError'

defineOptions({ name: 'ShiftClosureCorrectionsDialog' })

const manualFields = [
  { name: 'actualPartWeightGrams', label: 'Вес изделия, г', precision: 2 },
  { name: 'manualFullCycleSeconds', label: 'Ручной полный цикл, с', precision: 0 },
  { name: 'actualPackagingUnit', label: 'Тарная единица', precision: 0 },
  { name: 'packagingPlaces', label: 'Количество мест', precision: 2 },
  { name: 'manualGoodQuantity', label: 'Факт отлитых (годные)', precision: 0 },
  { name: 'manualDefectQuantity', label: 'Брак', precision: 0 }
]

const fieldErrorMessages = {
  required: 'Поле обязательно для заполнения',
  mustBePositive: 'Значение должно быть больше нуля',
  mustBeNonNegative: 'Значение не может быть отрицательным',
  maxTwoDecimalPlaces: 'Не больше двух знаков после запятой'
}

const props = defineProps({
  report: { type: Object, default: null },
  reloadReport: { type: Function, required: true }
})

const permissions = usePermissions()
const visible = ref(false)
const reason = ref('')
const reasonError = ref('')
const entries = ref([])
const history = ref([])
const historyLoading = ref(false)
const saving = ref(false)
const fieldErrors = ref({})
let historySequence = 0

const closed = computed(() => props.report?.state === 'Closed' && !!props.report?.id)
const canCorrect = computed(() => closed.value && permissions.canAccess(['Manager', 'Admin']))

function resetForm() {
  reason.value = ''
  reasonError.value = ''
  fieldErrors.value = {}
  entries.value = (props.report?.entries ?? []).map(entry => ({
    taskId: entry.taskId,
    immName: entry.immName,
    productName: entry.productName,
    ...Object.fromEntries(manualFields.map(field => [field.name, entry[field.name] ?? null]))
  }))
}

async function loadHistory() {
  if (!closed.value) return false
  const sequence = ++historySequence
  historyLoading.value = true
  try {
    const response = await shiftClosuresApi.getCorrections(props.report.id)
    if (sequence === historySequence) history.value = response.data ?? []
    return sequence === historySequence
  } catch (error) {
    if (sequence === historySequence) {
      ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить историю исправлений'))
    }
    return false
  } finally {
    if (sequence === historySequence) historyLoading.value = false
  }
}

async function openDialog() {
  if (!closed.value) return
  resetForm()
  visible.value = true
  await loadHistory()
}

function updateManual(taskId, field, value) {
  const entry = entries.value.find(item => item.taskId === taskId)
  if (!entry || !manualFields.some(item => item.name === field)) return
  entry[field] = value ?? null
  const key = `${taskId}:${field}`
  if (fieldErrors.value[key]) {
    const next = { ...fieldErrors.value }
    delete next[key]
    fieldErrors.value = next
  }
}

async function reloadData() {
  const [reportReloaded, historyReloaded] = await Promise.all([
    Promise.resolve().then(() => props.reloadReport()).catch(error => {
      ElMessage.error(apiErrorMessage(error, 'Не удалось обновить сменный рапорт'))
      return false
    }),
    loadHistory()
  ])
  const refreshed = reportReloaded !== false && historyReloaded
  if (refreshed && visible.value) {
    await nextTick()
    entries.value = (props.report?.entries ?? []).map(entry => ({
      taskId: entry.taskId,
      immName: entry.immName,
      productName: entry.productName,
      ...Object.fromEntries(manualFields.map(field => [field.name, entry[field.name] ?? null]))
    }))
  }
  return refreshed
}

async function submit() {
  if (!canCorrect.value || saving.value) return
  const trimmedReason = reason.value.trim()
  if (!trimmedReason) {
    reasonError.value = 'Укажите причину исправления'
    return
  }
  saving.value = true
  reasonError.value = ''
  fieldErrors.value = {}
  try {
    await shiftClosuresApi.correct(props.report.id, trimmedReason, entries.value.map(entry => ({
      shiftTaskId: entry.taskId,
      ...Object.fromEntries(manualFields.map(field => [field.name, entry[field.name] ?? null]))
    })))
    const refreshed = await reloadData()
    if (!refreshed) {
      ElMessage.error('Исправление сохранено, но не удалось обновить рапорт или историю. Обновите данные перед продолжением.')
      return
    }
    ElMessage.success('Показатели сменного рапорта исправлены')
    visible.value = false
  } catch (error) {
    if (error?.response?.status === 409) {
      ElMessage.error(apiErrorMessage(error, 'Сменный рапорт изменён другим пользователем'))
      await reloadData()
      return
    }
    const errors = error?.response?.data?.errors
    if (Array.isArray(errors) && errors.length > 0) {
      fieldErrors.value = Object.fromEntries(errors.map(item => [
        `${item.taskId}:${item.field}`,
        fieldErrorMessages[item.code] ?? item.code ?? 'Некорректное значение'
      ]))
      return
    }
    ElMessage.error(apiErrorMessage(error, 'Не удалось исправить сменный рапорт'))
  } finally {
    saving.value = false
  }
}

function fieldLabel(field) {
  return manualFields.find(item => item.name === field)?.label ?? field
}

function formatValue(value) {
  return value === null || value === undefined ? '—' : String(value)
}

function formatDateTime(value) {
  return value ? dayjs(value).format('DD.MM.YYYY HH:mm') : '—'
}
</script>
