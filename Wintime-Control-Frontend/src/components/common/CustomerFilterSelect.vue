<template>
  <el-select
    :model-value="selection"
    filterable
    remote
    :debounce="0"
    clearable
    :remote-method="onRemoteMethod"
    :loading="loading"
    :placeholder="placeholder"
    class="w-full"
    @update:model-value="selectKey"
  >
    <el-option
      v-for="option in options"
      :key="option.key"
      :label="option.label"
      :value="option.key"
    />
  </el-select>
</template>

<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import { ElMessage } from 'element-plus'

const EMPTY_KEY = 'empty'
const CUSTOMER_PREFIX = 'customer:'

const props = defineProps({
  customer: { type: String, default: null },
  customerEmpty: { type: Boolean, default: false },
  fetch: { type: Function, required: true },
  placeholder: { type: String, default: 'Все' }
})
const emit = defineEmits(['update:customer', 'update:customerEmpty'])

const suggestions = ref([])
const loading = ref(false)
let timer = null
let suggestionsRequestId = 0

const selection = computed(() => {
  if (props.customerEmpty) return EMPTY_KEY
  return props.customer ? CUSTOMER_PREFIX + props.customer : ''
})

const options = computed(() => {
  const names = [...new Set(suggestions.value)]
  if (props.customer && !names.includes(props.customer)) names.unshift(props.customer)
  return [
    { key: EMPTY_KEY, label: '(не указано)' },
    ...names.map(name => ({ key: CUSTOMER_PREFIX + name, label: name }))
  ]
})

async function loadSuggestions(search, requestId = ++suggestionsRequestId) {
  loading.value = true
  try {
    const result = await props.fetch(search ?? '')
    if (requestId === suggestionsRequestId) suggestions.value = result ?? []
  } catch {
    if (requestId === suggestionsRequestId) {
      ElMessage.error('Ошибка загрузки заказчиков')
    }
  } finally {
    if (requestId === suggestionsRequestId) loading.value = false
  }
}

function onRemoteMethod(search) {
  const requestId = ++suggestionsRequestId
  clearTimeout(timer)
  timer = setTimeout(() => loadSuggestions(search, requestId), 300)
}

function selectKey(value) {
  if (!value) {
    emit('update:customer', null)
    emit('update:customerEmpty', false)
    return
  }
  if (value === EMPTY_KEY) {
    emit('update:customer', null)
    emit('update:customerEmpty', true)
    return
  }
  if (!value.startsWith(CUSTOMER_PREFIX)) return
  emit('update:customer', value.slice(CUSTOMER_PREFIX.length))
  emit('update:customerEmpty', false)
}

onBeforeUnmount(() => {
  suggestionsRequestId += 1
  clearTimeout(timer)
})
defineExpose({ loadSuggestions, onRemoteMethod, selectKey, options })
</script>
