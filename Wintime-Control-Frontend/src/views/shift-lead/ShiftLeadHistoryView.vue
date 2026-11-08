<template>
  <div v-loading="store.historyLoading" class="min-h-[160px]">
    <div class="mb-4 flex items-center justify-between gap-3">
      <div>
        <h2 class="text-xl font-bold text-gray-800">История смен</h2>
        <p class="mt-1 text-sm text-gray-600">ТПА, задания и литейщики в режиме чтения</p>
      </div>
      <el-button
        data-test="refresh-history"
        :loading="store.historyLoading"
        @click="store.loadHistory"
      >
        Обновить
      </el-button>
    </div>

    <div v-if="store.history.length" class="space-y-3">
      <button
        v-for="item in store.history"
        :key="item.itemId"
        type="button"
        :data-test="`history-item-${item.itemId}`"
        class="w-full rounded-lg border border-gray-200 bg-white p-4 text-left shadow-sm hover:border-primary-400"
        @click="openItem(item.itemId)"
      >
        <div class="flex flex-wrap items-center justify-between gap-2">
          <span class="font-semibold text-gray-800">
            {{ formatDate(item.shiftDate) }} · смена {{ item.position }}
          </span>
          <span class="text-sm text-gray-500">{{ stateLabel(item.state) }}</span>
        </div>
        <div class="mt-2 text-sm text-gray-600">
          {{ formatDateTime(item.startAtUtc) }} — {{ formatDateTime(item.endAtUtc) }}
        </div>
      </button>
    </div>
    <el-empty v-else-if="!store.historyLoading" description="История смен пуста" />
  </div>
</template>

<script setup>
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import dayjs from 'dayjs'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'

const store = useShiftStaffingStore()
const router = useRouter()

onMounted(store.loadHistory)

function formatDate(value) {
  return value ? dayjs(value).format('DD.MM.YYYY') : '—'
}

function formatDateTime(value) {
  return value ? dayjs(value).format('DD.MM.YYYY HH:mm') : '—'
}

function stateLabel(state) {
  return { Historical: 'Завершена', Current: 'Текущая', Future: 'Предстоящая' }[state] ?? state
}

function openItem(itemId) {
  router.push(`/shift-lead/items/${itemId}`)
}
</script>
