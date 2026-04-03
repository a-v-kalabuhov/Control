<template>
  <el-tag 
    :type="tagType" 
    :effect="effect"
    size="small"
    class="font-medium"
  >
    {{ label }}
  </el-tag>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  status: {
    type: String,
    required: true,
    validator: (value) => ['Draft', 'Issued', 'InProgress', 'Completed', 'Closed'].includes(value)
  }
})

const statusConfig = {
  Draft: { 
    label: 'Черновик', 
    type: 'info', 
    effect: 'plain' 
  },
  Issued: { 
    label: 'Выдано', 
    type: 'primary', 
    effect: 'light' 
  },
  InProgress: { 
    label: 'В работе', 
    type: 'warning', 
    effect: 'light' 
  },
  Completed: { 
    label: 'Выполнено', 
    type: 'success', 
    effect: 'light' 
  },
  Closed: { 
    label: 'Закрыто', 
    type: 'info', 
    effect: 'plain' 
  }
}

const config = computed(() => statusConfig[props.status] || statusConfig.Draft)
const tagType = computed(() => config.value.type)
const effect = computed(() => config.value.effect)
const label = computed(() => config.value.label)
</script>