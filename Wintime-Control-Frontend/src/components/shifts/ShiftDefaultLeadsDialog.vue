<template>
  <el-dialog
    v-model="visible"
    title="Настройка графика смен"
    width="1100px"
    :teleported="false"
    :close-on-click-modal="false"
  >
    <el-alert
      v-if="errorMessage"
      data-test="default-leads-error"
      type="error"
      :title="errorMessage"
      :closable="false"
      class="mb-4"
    >
      <el-button
        v-if="scheduleConflict"
        data-test="reload-default-leads"
        type="primary"
        plain
        size="small"
        :loading="loading"
        @click="load"
      >
        Перезагрузить
      </el-button>
    </el-alert>

    <el-table v-loading="loading" :data="items" stripe style="width: 100%">
      <el-table-column prop="number" label="№" width="60" align="center" />
      <el-table-column label="Начало" width="90" align="center">
        <template #default="{ row }">{{ minutesToTime(row.startMinutes) }}</template>
      </el-table-column>
      <el-table-column label="Конец" width="100" align="center">
        <template #default="{ row }">
          {{ minutesToTime(row.startMinutes + row.durationMinutes) }}
          <el-tag v-if="isNightShift(row)" type="info" size="small" class="ml-1">+1</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Длительность" width="130" align="center">
        <template #default="{ row }">{{ formatDuration(row.durationMinutes) }}</template>
      </el-table-column>
      <el-table-column label="Перерыв" min-width="170">
        <template #default="{ row }">
          <span v-if="row.breakDurationMinutes > 0">
            {{ minutesToTime(row.breakStartMinutes) }} – {{ minutesToTime(row.breakStartMinutes + row.breakDurationMinutes) }}
            <span class="text-gray-500">({{ formatDuration(row.breakDurationMinutes) }})</span>
          </span>
          <span v-else class="text-gray-400">—</span>
        </template>
      </el-table-column>
      <el-table-column label="Старший смены" min-width="270">
        <template #default="{ row }">
          <el-select
            v-model="row.selectedShiftLeadId"
            clearable
            filterable
            placeholder="Не задан"
            class="w-full"
            :disabled="!loaded || loading || saving"
          >
            <el-option
              v-for="candidate in optionsFor(row)"
              :key="candidate.id"
              :label="candidateLabel(candidate)"
              :value="candidate.id"
            />
          </el-select>
        </template>
      </el-table-column>
    </el-table>

    <p v-if="loaded && fallbackManager" class="mt-4 text-sm text-gray-600">
      Если старший не задан, используется первый активный менеджер:
      <span class="font-medium text-gray-800">{{ fallbackManager.fullName }}</span>
    </p>
    <el-alert
      v-else-if="loaded"
      class="mt-4"
      type="warning"
      title="Нет активных менеджеров для автоматического выбора старшего"
      :closable="false"
    />

    <template #footer>
      <el-button data-test="cancel-default-leads" :disabled="saving" @click="cancel">
        Отмена
      </el-button>
      <el-button
        data-test="save-default-leads"
        type="primary"
        :loading="saving"
        :disabled="!loaded || loading || saving"
        @click="save"
      >
        Сохранить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { shiftsApi } from '@/api/shifts'
import { shiftPlansApi } from '@/api/shiftPlans'
import { apiErrorMessage } from '@/utils/apiError'

defineOptions({ name: 'ShiftDefaultLeadsDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false }
})
const emit = defineEmits(['update:modelValue', 'saved'])

const visible = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
})
const items = ref([])
const candidates = ref([])
const fallbackManager = ref(null)
const loading = ref(false)
const saving = ref(false)
const loaded = ref(false)
const errorMessage = ref('')
const scheduleConflict = ref(false)
let contextVersion = 0
let loadSequence = 0

watch(() => props.modelValue, isOpen => {
  contextVersion++
  loadSequence++
  saving.value = false
  if (isOpen) {
    load()
  } else {
    resetState()
  }
}, { immediate: true })

onBeforeUnmount(() => {
  contextVersion++
  loadSequence++
})

async function load() {
  const context = contextVersion
  const request = ++loadSequence
  resetState()
  loading.value = true
  try {
    const [settingsResponse, candidatesResponse] = await Promise.all([
      shiftsApi.getDefaultLeads(),
      shiftPlansApi.getLeadCandidates()
    ])
    if (context !== contextVersion || request !== loadSequence) return
    candidates.value = candidatesResponse.data
    applySettings(settingsResponse.data)
  } catch (error) {
    if (context !== contextVersion || request !== loadSequence) return
    errorMessage.value = apiErrorMessage(error, 'Не удалось загрузить настройки старших смен')
  } finally {
    if (context === contextVersion && request === loadSequence) loading.value = false
  }
}

function resetState() {
  items.value = []
  candidates.value = []
  fallbackManager.value = null
  loading.value = false
  loaded.value = false
  errorMessage.value = ''
  scheduleConflict.value = false
}

function applySettings(data) {
  items.value = data.items.map(item => ({
    ...item,
    selectedShiftLeadId: item.defaultShiftLeadId
  }))
  fallbackManager.value = data.fallbackManager
  loaded.value = true
}

async function save() {
  if (saving.value || loading.value || !loaded.value || !props.modelValue) return
  const context = contextVersion
  saving.value = true
  errorMessage.value = ''
  scheduleConflict.value = false
  try {
    const payload = items.value.map(item => ({
      position: item.position,
      shiftLeadId: item.selectedShiftLeadId ?? null
    }))
    const response = await shiftsApi.saveDefaultLeads(payload)
    if (context !== contextVersion) return
    applySettings(response.data)
    ElMessage.success('Старшие смен по умолчанию сохранены')
    emit('saved', response.data)
    visible.value = false
  } catch (error) {
    if (context !== contextVersion) return
    scheduleConflict.value = error?.response?.status === 409
    errorMessage.value = apiErrorMessage(
      error,
      scheduleConflict.value
        ? 'График смен изменился. Перезагрузите настройки.'
        : 'Не удалось сохранить старших смен по умолчанию'
    )
  } finally {
    if (context === contextVersion) saving.value = false
  }
}

function cancel() {
  visible.value = false
}

function optionsFor(item) {
  const result = [...candidates.value]
  const selectedId = item.selectedShiftLeadId
  if (selectedId && selectedId === item.defaultShiftLeadId &&
      !result.some(candidate => candidate.id === selectedId)) {
    result.unshift({
      id: selectedId,
      fullName: item.defaultShiftLeadName || selectedId,
      employeeId: item.defaultShiftLeadEmployeeId,
      archived: true
    })
  }
  return result
}

function candidateLabel(candidate) {
  const employee = candidate.employeeId ? ` (${candidate.employeeId})` : ''
  const archived = candidate.archived ? ' — архив' : ''
  return `${candidate.fullName}${employee}${archived}`
}

function minutesToTime(totalMinutes) {
  const hours = Math.floor(totalMinutes / 60) % 24
  const minutes = totalMinutes % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`
}

function formatDuration(minutes) {
  const hours = Math.floor(minutes / 60)
  const remainder = minutes % 60
  return remainder === 0 ? `${hours} ч` : `${hours} ч ${remainder} мин`
}

function isNightShift(item) {
  return item.startMinutes + item.durationMinutes >= 1440
}
</script>
