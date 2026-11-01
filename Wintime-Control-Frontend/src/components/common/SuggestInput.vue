<template>
  <el-autocomplete
    :model-value="modelValue"
    :fetch-suggestions="querySearch"
    :placeholder="placeholder"
    :trigger-on-focus="true"
    clearable
    class="w-full"
    @update:model-value="v => emit('update:modelValue', v ?? '')"
    @select="onSelect"
  >
    <template #default="{ item }">
      <span>{{ item.label }}</span>
    </template>
  </el-autocomplete>
</template>

<script setup>
// Свободный текст с подсказками ранее введённых значений (PZP-22; переиспользуют PZP-23, PZP-28).
// Значение не из списка разрешено: подсказки только помогают не плодить разные написания.
const props = defineProps({
  modelValue: { type: String, default: '' },
  fetch: { type: Function, required: true },          // (search) => Promise<item[]>
  itemLabel: { type: Function, default: item => String(item) },
  itemValue: { type: Function, default: item => String(item) },
  placeholder: { type: String, default: '' }
})

const emit = defineEmits(['update:modelValue', 'select'])

async function querySearch(search, cb) {
  try {
    const items = await props.fetch(search ?? '')
    cb((items ?? []).map(item => ({ value: props.itemValue(item), label: props.itemLabel(item), item })))
  } catch {
    cb([])
  }
}

function onSelect(suggestion) {
  emit('update:modelValue', suggestion.value)
  emit('select', suggestion.item)
}

defineExpose({ querySearch, onSelect })
</script>
