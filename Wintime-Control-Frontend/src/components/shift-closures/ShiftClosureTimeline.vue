<template>
  <section class="shift-closure-timeline" :data-test="`timeline-${timeline.immId}`">
    <h4 class="mb-2 text-sm font-semibold text-gray-700">Временная шкала</h4>
    <div v-if="timeline.intervals?.length" class="space-y-2">
      <article
        v-for="(interval, index) in timeline.intervals"
        :key="`${interval.startAtUtc}-${interval.endAtUtc}-${index}`"
        :data-test="`timeline-interval-${index}`"
        :data-orphan="isOrphan(interval)"
        class="rounded border px-3 py-2 text-sm"
        :class="isOrphan(interval) ? 'border-red-300 bg-red-50' : 'border-gray-200 bg-gray-50'"
      >
        <div class="flex flex-wrap items-start justify-between gap-2">
          <div>
            <div class="font-medium text-gray-800">
              {{ formatDateTime(interval.startAtUtc) }} — {{ formatDateTime(interval.endAtUtc) }}
            </div>
            <div class="mt-1 text-gray-600">
              <span v-if="interval.operators?.length">
                <span
                  v-for="(operator, operatorIndex) in interval.operators"
                  :key="operator.operatorId"
                >
                  {{ operatorIndex ? ', ' : '' }}{{ operator.fullName }}<template v-if="operator.employeeId"> ({{ operator.employeeId }})</template>
                </span>
              </span>
              <span v-else>Литейщики не назначены</span>
            </div>
            <div class="mt-1 text-gray-700">
              Автоматический выпуск: <span class="font-medium">{{ interval.automaticOutputQuantity }}</span>
            </div>
            <p v-if="isOrphan(interval)" class="mt-1 text-red-700">
              Есть выпуск без назначенного литейщика
            </p>
          </div>
          <el-button
            v-if="isOrphan(interval) && canEdit"
            type="primary"
            size="small"
            :data-test="`assign-orphan-${index}`"
            :disabled="disabled"
            @click="assignOrphan(interval)"
          >
            Назначить
          </el-button>
        </div>
      </article>
    </div>
    <p v-else class="text-sm text-gray-500">Интервалов нет</p>
  </section>
</template>

<script setup>
import dayjs from 'dayjs'

defineOptions({ name: 'ShiftClosureTimeline' })

const props = defineProps({
  timeline: { type: Object, required: true },
  canEdit: { type: Boolean, default: true },
  disabled: { type: Boolean, default: false }
})

const emit = defineEmits(['assign'])

function formatDateTime(value) {
  return dayjs(value).format('DD.MM.YYYY HH:mm')
}

function isOrphan(interval) {
  return !(interval.operators?.length) && interval.automaticOutputQuantity > 0
}

function assignOrphan(interval) {
  emit('assign', {
    immId: props.timeline.immId,
    assignedAt: interval.startAtUtc,
    unassignedAt: interval.endAtUtc
  })
}
</script>
