<template>
  <el-badge
    v-if="store.taskCount > 0"
    :value="store.taskCount"
    :type="store.planCount > 0 ? 'danger' : 'warning'"
    class="ml-2"
    data-test="plan-conflicts-badge"
    :title="`Требуют корректировки: ${store.taskCount}`"
  />
</template>

<script setup>
import { usePlanConflictsStore } from '@/stores/planConflicts'

// Бейдж «N заданий требуют корректировки» (PZP-07b): красный — есть конфликт плана, иначе оранжевый.
const store = usePlanConflictsStore()
</script>
