<template>
  <el-dialog
    v-if="modelValue"
    v-model="visible"
    :title="title"
    width="480px"
    :teleported="false"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <div v-if="assignment" class="mb-3 text-sm text-gray-600">
      {{ assignment.operatorName }}
    </div>
    <el-form label-position="top" @submit.prevent="submit">
      <el-form-item v-if="mode === 'create'" label="Литейщик">
        <el-select
          v-model="selectedOperatorId"
          data-test="operator-select"
          class="w-full"
          filterable
          placeholder="Выберите литейщика"
          :loading="operatorsLoading"
          :disabled="controlsDisabled || operatorsLoading"
        >
          <el-option
            v-for="operator in operators"
            :key="operator.id"
            :value="operator.id"
            :label="operatorLabel(operator)"
          />
        </el-select>
      </el-form-item>

      <el-form-item v-if="mode !== 'unassign'" label="Начало работы">
        <input
          v-model="assignedAt"
          data-test="assigned-at"
          type="datetime-local"
          class="w-full rounded border border-gray-300 px-3 py-2"
          :disabled="controlsDisabled"
        >
        <p v-if="mode === 'create'" class="mt-1 text-xs text-gray-500">
          Если пусто, сервер использует начало смены
        </p>
      </el-form-item>

      <el-form-item label="Окончание работы">
        <input
          v-model="unassignedAt"
          data-test="unassigned-at"
          type="datetime-local"
          class="w-full rounded border border-gray-300 px-3 py-2"
          :disabled="controlsDisabled"
        >
        <p class="mt-1 text-xs text-gray-500">
          {{ endHint }}
        </p>
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button :disabled="store.saving" @click="visible = false">Отмена</el-button>
      <el-button
        type="primary"
        data-test="submit-interval"
        :disabled="!canSubmit"
        :loading="store.saving"
        @click="submit"
      >
        {{ submitLabel }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'

defineOptions({ name: 'OperatorIntervalDialog' })

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  mode: {
    type: String,
    default: 'create',
    validator: value => ['create', 'unassign', 'edit'].includes(value)
  },
  itemId: { type: String, required: true },
  immId: { type: String, default: null },
  assignment: { type: Object, default: null },
  initialInterval: { type: Object, default: null },
  operators: { type: Array, default: () => [] },
  operatorsLoading: { type: Boolean, default: false },
  canEdit: { type: Boolean, default: true }
})

const emit = defineEmits(['update:modelValue', 'saved', 'reload'])
const store = useShiftStaffingStore()
const selectedOperatorId = ref(null)
const assignedAt = ref('')
const unassignedAt = ref('')

const visible = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
})
const controlsDisabled = computed(() => !props.canEdit || store.saving)
const title = computed(() => ({
  create: 'Назначить литейщика',
  unassign: 'Снять литейщика',
  edit: 'Изменить интервал литейщика'
}[props.mode]))
const submitLabel = computed(() => ({
  create: 'Назначить', unassign: 'Снять', edit: 'Сохранить'
}[props.mode]))
const endHint = computed(() => {
  if (props.mode === 'unassign') return 'Если пусто, сервер выберет время снятия'
  if (props.mode === 'create') return 'Если пусто, сервер использует окончание смены'
  return 'Если пусто, интервал продолжается до окончания смены'
})
const canSubmit = computed(() => {
  if (controlsDisabled.value || props.operatorsLoading) return false
  if (props.mode === 'create') return !!selectedOperatorId.value
  if (props.mode === 'edit') return !!assignedAt.value
  return !!props.assignment?.id
})

watch(() => [
  props.modelValue,
  props.mode,
  props.itemId,
  props.immId,
  props.assignment?.id,
  props.initialInterval?.assignedAt,
  props.initialInterval?.unassignedAt
], resetForm, { immediate: true })

function resetForm() {
  selectedOperatorId.value = props.mode === 'create' ? null : props.assignment?.operatorId ?? null
  const start = props.mode === 'create'
    ? props.initialInterval?.assignedAt
    : props.assignment?.assignedAt
  const end = props.mode === 'create'
    ? props.initialInterval?.unassignedAt
    : props.mode === 'edit' ? props.assignment?.unassignedAt : null
  assignedAt.value = toInput(start)
  unassignedAt.value = toInput(end)
}

function toInput(value) {
  return value ? dayjs(value).format('YYYY-MM-DDTHH:mm') : ''
}

function toUtc(value) {
  return value ? dayjs(value).toISOString() : null
}

function operatorLabel(operator) {
  return operator.employeeId ? `${operator.fullName} (${operator.employeeId})` : operator.fullName
}

async function submit() {
  if (!canSubmit.value) return
  let result
  if (props.mode === 'create') {
    result = await store.assignOperator(props.itemId, {
      immId: props.immId,
      operatorId: selectedOperatorId.value,
      assignedAt: toUtc(assignedAt.value),
      unassignedAt: toUtc(unassignedAt.value)
    })
  } else if (props.mode === 'unassign') {
    const end = toUtc(unassignedAt.value)
    result = await store.unassignOperator(props.assignment.id, end ? { unassignedAt: end } : {})
  } else {
    result = await store.updateOperatorInterval(props.assignment.id, {
      assignedAt: toUtc(assignedAt.value),
      unassignedAt: toUtc(unassignedAt.value)
    })
  }

  if (result.pending || result.stale) return
  emit('reload')
  if (result.success) {
    emit('saved', result.data)
    visible.value = false
  }
}
</script>
