<template>
  <el-alert
    v-if="store.taskCount > 0"
    :type="store.planCount > 0 ? 'error' : 'warning'"
    :closable="false"
    show-icon
    class="mb-4"
    data-test="plan-conflicts-banner"
  >
    <template #title>
      <span>{{ title }}</span>
      <el-button link type="primary" class="ml-2" data-test="plan-conflicts-toggle" @click="expanded = !expanded">
        {{ expanded ? 'Скрыть' : 'Показать' }}
      </el-button>
    </template>
    <ul v-if="expanded" class="mt-2 space-y-1">
      <li
        v-for="(c, i) in store.items"
        :key="i"
        class="cursor-pointer hover:underline"
        :style="{ color: CONFLICT_KIND_COLORS[c.kind] }"
        data-test="plan-conflict-row"
        @click="$emit('select', c.task)"
      >
        {{ taskRefLabel(c.task) }} — {{ conflictLine(c, c.task.id) }}
      </li>
    </ul>
  </el-alert>
</template>

<script setup>
import { ref, computed } from 'vue'
import { usePlanConflictsStore } from '@/stores/planConflicts'
import { CONFLICT_KIND_COLORS, taskRefLabel, conflictLine } from '@/utils/planConflicts'

// Баннер «N заданий требуют корректировки» на Гантте (PZP-07b); клик по строке — задание на доске и его карточка.
defineEmits(['select'])

const store = usePlanConflictsStore()
const expanded = ref(false)

// 1 задание / 2 задания / 5 заданий
function plural(n, one, few, many) {
  const m10 = n % 10
  const m100 = n % 100
  if (m10 === 1 && m100 !== 11) return one
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few
  return many
}

const title = computed(() => {
  const n = store.taskCount
  const parts = []
  if (store.planCount) parts.push(`${store.planCount} ${plural(store.planCount, 'конфликт плана', 'конфликта плана', 'конфликтов плана')}`)
  if (store.deviationCount) parts.push(`${store.deviationCount} ${plural(store.deviationCount, 'конфликт отклонения', 'конфликта отклонения', 'конфликтов отклонения')}`)
  const verb = plural(n, 'требует', 'требуют', 'требуют')
  return `${n} ${plural(n, 'задание', 'задания', 'заданий')} ${verb} корректировки: ${parts.join(', ')}`
})
</script>
