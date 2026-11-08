<template>
  <el-dialog
    v-model="visible"
    :title="`Смена ${item?.position ?? ''}`"
    width="480px"
    :close-on-click-modal="false"
    append-to-body
  >
    <div v-if="item" class="mb-4 text-sm text-gray-600">
      <div>{{ formatDate(day?.shiftDate) }}</div>
      <div>{{ formatShiftWindow(item) }} ({{ item.timeZoneId }})</div>
    </div>

    <el-form label-position="top">
      <el-form-item label="Старший смены">
        <el-select
          v-model="selectedLeadId"
          data-test="lead-select"
          filterable
          class="w-full"
          placeholder="Выберите старшего смены"
          :disabled="!canEdit"
          :loading="isSaving"
        >
          <el-option
            v-for="candidate in options"
            :key="candidate.id"
            :label="candidateLabel(candidate)"
            :value="candidate.id"
          />
        </el-select>
      </el-form-item>
    </el-form>

    <router-link
      v-if="item?.id && item?.shiftLeadId"
      :to="`/shift-lead/items/${item.id}`"
      class="text-sm text-primary-600 hover:text-primary-800"
      data-test="open-staffing"
    >
      Открыть ТПА и литейщиков
    </router-link>

    <template #footer>
      <el-button @click="visible = false">Закрыть</el-button>
      <el-button
        data-test="clear-lead"
        type="danger"
        plain
        :disabled="!canClear"
        :loading="isSaving"
        @click="clearLead"
      >
        Очистить
      </el-button>
      <el-button
        data-test="save-lead"
        type="primary"
        :disabled="!canSave"
        :loading="isSaving"
        @click="saveLead"
      >
        Назначить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
import { shiftPlansApi } from '@/api/shiftPlans'
import { apiErrorMessage } from '@/utils/apiError'

dayjs.extend(utc)
dayjs.extend(timezone)

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  day: { type: Object, default: null },
  item: { type: Object, default: null },
  candidates: { type: Array, default: () => [] },
  saving: { type: Boolean, default: false }
})

const emit = defineEmits(['update:modelValue', 'saved', 'reload'])
const selectedLeadId = ref(null)
const requestSaving = ref(false)
let contextVersion = 0
let saveSequence = 0

const visible = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
})
const isSaving = computed(() => props.saving || requestSaving.value)
const canEdit = computed(() => props.item?.canEditLead === true && !isSaving.value)
const canSave = computed(() => canEdit.value && !!selectedLeadId.value)
const canClear = computed(() => canEdit.value && !!props.item?.shiftLeadId &&
  (props.item?.operatorAssignmentCount ?? 0) === 0)

const options = computed(() => {
  const result = [...props.candidates]
  if (props.item?.shiftLeadId && !result.some(c => c.id === props.item.shiftLeadId)) {
    result.unshift({
      id: props.item.shiftLeadId,
      fullName: props.item.shiftLeadName || props.item.shiftLeadId,
      employeeId: null
    })
  }
  return result
})

watch(() => [
  props.modelValue,
  props.day?.shiftDate,
  props.item?.position,
  props.item?.shiftLeadId
], () => {
  contextVersion++
  saveSequence++
  requestSaving.value = false
  selectedLeadId.value = props.item?.shiftLeadId ?? null
}, { immediate: true })

onBeforeUnmount(() => {
  contextVersion++
  saveSequence++
})

function candidateLabel(candidate) {
  return candidate.employeeId
    ? `${candidate.fullName} (${candidate.employeeId})`
    : candidate.fullName
}

function formatDate(value) {
  return value ? dayjs(value).format('DD.MM.YYYY') : ''
}

function formatShiftWindow(item) {
  const start = dayjs(item.startAtUtc).tz(item.timeZoneId).format('HH:mm')
  const end = dayjs(item.endAtUtc).tz(item.timeZoneId).format('HH:mm')
  return `${start}–${end}`
}

async function saveLead() {
  if (!canSave.value) return
  await persist(selectedLeadId.value)
}

async function clearLead() {
  if (!canClear.value) return
  await persist(null)
}

async function persist(shiftLeadId) {
  if (!props.day || !props.item || requestSaving.value) return
  const date = props.day.shiftDate
  const position = props.item.position
  const version = contextVersion
  const request = ++saveSequence
  const isCurrent = () => request === saveSequence && version === contextVersion &&
    props.modelValue && props.day?.shiftDate === date && props.item?.position === position

  requestSaving.value = true
  try {
    const { data } = await shiftPlansApi.setLead(date, position, shiftLeadId)
    if (!isCurrent()) return
    selectedLeadId.value = shiftLeadId
    emit('saved', data, { date, position, request })
  } catch (error) {
    if (!isCurrent()) return
    ElMessage.error(apiErrorMessage(error, 'Не удалось назначить старшего смены'))
    if (error?.response?.status === 409) emit('reload')
  } finally {
    if (request === saveSequence) requestSaving.value = false
  }
}
</script>
