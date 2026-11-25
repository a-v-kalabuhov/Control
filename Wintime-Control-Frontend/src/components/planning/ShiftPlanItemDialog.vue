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
      <div class="mt-2 flex flex-wrap gap-2">
        <span
          v-if="item.closureStatus === 'Closed'"
          data-test="closure-status"
          class="rounded bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800"
        >Закрыта</span>
        <span
          v-else-if="item.closureStatus === 'NotClosed'"
          data-test="closure-status"
          class="rounded bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800"
        >Не закрыта</span>
        <span
          v-if="item.hasOrphanOutput === true"
          data-test="orphan-output"
          class="rounded bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800"
        >Есть выпуск без литейщика</span>
        <span
          v-if="item.hasMissingLeadWarning === true"
          data-test="missing-lead"
          class="rounded bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800"
        >Не назначен старший</span>
      </div>
    </div>

    <el-form label-position="top">
      <el-form-item label="Старший смены">
        <el-select
          :model-value="selectedExplicitLeadId"
          data-test="lead-select"
          filterable
          class="w-full"
          :placeholder="emptyLeadOptionLabel"
          :disabled="!canEdit"
          :loading="isSaving"
          @update:model-value="selectLead"
        >
          <el-option :label="emptyLeadOptionLabel" value="" />
          <el-option
            v-for="candidate in options"
            :key="candidate.id"
            :label="candidateLabel(candidate)"
            :value="candidate.id"
          />
        </el-select>
      </el-form-item>
    </el-form>

    <div class="flex flex-wrap gap-x-4 gap-y-2">
      <router-link
        v-if="item?.id && item?.shiftLeadId"
        :to="`/shift-lead/items/${item.id}`"
        class="text-sm text-primary-600 hover:text-primary-800"
        data-test="open-staffing"
      >
        Открыть ТПА и литейщиков
      </router-link>
      <button
        v-else-if="item?.shiftLeadId"
        type="button"
        class="text-sm text-primary-600 hover:text-primary-800 disabled:opacity-50"
        data-test="open-staffing"
        :disabled="isSaving"
        @click="materializeAndOpen('staffing')"
      >
        Открыть ТПА и литейщиков
      </button>
      <router-link
        v-if="item?.id && item?.shiftLeadId"
        :to="`/shift-lead/items/${item.id}/report`"
        class="text-sm text-primary-600 hover:text-primary-800"
        data-test="open-closure"
      >
        Сменный рапорт
      </router-link>
      <button
        v-else-if="item?.shiftLeadId"
        type="button"
        class="text-sm text-primary-600 hover:text-primary-800 disabled:opacity-50"
        data-test="open-closure"
        :disabled="isSaving"
        @click="materializeAndOpen('closure')"
      >
        Сменный рапорт
      </button>
    </div>

    <template #footer>
      <el-button @click="visible = false">Закрыть</el-button>
      <el-button
        v-if="canSelfAssign"
        data-test="self-assign-lead"
        type="primary"
        plain
        :disabled="isSaving"
        :loading="requestSaving"
        @click="selfAssignLead"
      >
        Назначить себя старшим
      </el-button>
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
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
import { shiftPlansApi } from '@/api/shiftPlans'
import { useAuthStore } from '@/stores/auth'
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
const authStore = useAuthStore()
const router = useRouter()
const selectedExplicitLeadId = ref(null)
const requestSaving = ref(false)
let contextVersion = 0
let saveSequence = 0

const visible = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
})
const isSaving = computed(() => props.saving || requestSaving.value)
const canEdit = computed(() => props.item?.canEditLead === true && !isSaving.value)
const canSelfAssign = computed(() => props.item?.canEditLead === true &&
  props.item?.shiftLeadSource !== 'Explicit' && authStore.isManager && !!authStore.user?.id)
const canSave = computed(() => canEdit.value && !!selectedExplicitLeadId.value)
const canClear = computed(() => canEdit.value && props.item?.shiftLeadSource === 'Explicit' &&
  !!props.item?.shiftLeadId && (props.item?.operatorAssignmentCount ?? 0) === 0)
const emptyLeadOptionLabel = computed(() => {
  if (props.item?.shiftLeadSource === 'Default' && props.item.shiftLeadName) {
    return `По умолчанию: ${props.item.shiftLeadName}`
  }
  if (props.item?.shiftLeadSource === 'ManagerFallback' && props.item.shiftLeadName) {
    return `Первый активный менеджер: ${props.item.shiftLeadName}`
  }
  return 'Не назначен'
})

const options = computed(() => {
  const result = [...props.candidates]
  const explicitId = props.item?.shiftLeadSource === 'Explicit' ? props.item.shiftLeadId : null
  if (explicitId && !result.some(c => c.id === explicitId)) {
    result.unshift({
      id: explicitId,
      fullName: props.item.shiftLeadName || explicitId,
      employeeId: null
    })
  }
  return result
})

watch(() => [
  props.modelValue,
  props.day?.shiftDate,
  props.item?.position,
  props.item?.shiftLeadId,
  props.item?.shiftLeadSource
], () => {
  contextVersion++
  saveSequence++
  requestSaving.value = false
  selectedExplicitLeadId.value = props.item?.shiftLeadSource === 'Explicit'
    ? props.item.shiftLeadId ?? null
    : null
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

function selectLead(value) {
  selectedExplicitLeadId.value = value || null
}

function formatDate(value) {
  return value ? dayjs(value).format('DD.MM.YYYY') : ''
}

function formatShiftWindow(item) {
  const start = dayjs(item.startAtUtc).tz(item.timeZoneId).format('HH:mm')
  const end = dayjs(item.endAtUtc).tz(item.timeZoneId).format('HH:mm')
  return `${start}–${end}`
}

async function selfAssignLead() {
  if (!canSelfAssign.value || isSaving.value) return
  await persist(authStore.user.id)
}

async function saveLead() {
  if (!canSave.value) return
  await persist(selectedExplicitLeadId.value)
}

async function clearLead() {
  if (!canClear.value) return
  await persist(null)
}

async function materializeAndOpen(destination) {
  if (!props.day || !props.item?.shiftLeadId || props.item.id || requestSaving.value) return
  const date = props.day.shiftDate
  const position = props.item.position
  const version = contextVersion
  const request = ++saveSequence
  const isCurrent = () => request === saveSequence && version === contextVersion &&
    props.modelValue && props.day?.shiftDate === date && props.item?.position === position

  requestSaving.value = true
  try {
    const { data } = await shiftPlansApi.materializeShift(date, position)
    if (!isCurrent()) return
    emit('saved', data, { date, position, request })
    const savedItem = data?.items?.find(item => item.position === position)
    if (!savedItem?.id) return
    const suffix = destination === 'closure' ? '/report' : ''
    await router.push(`/shift-lead/items/${savedItem.id}${suffix}`)
  } catch (error) {
    if (!isCurrent()) return
    ElMessage.error(apiErrorMessage(error, 'Не удалось сохранить смену'))
    if (error?.response?.status === 409) emit('reload')
  } finally {
    if (request === saveSequence) requestSaving.value = false
  }
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
    const savedItem = data?.items?.find(item => item.position === position)
    selectedExplicitLeadId.value = savedItem?.shiftLeadSource === 'Explicit'
      ? savedItem.shiftLeadId ?? null
      : null
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
