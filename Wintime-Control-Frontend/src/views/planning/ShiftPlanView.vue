<template>
  <div>
    <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">План смен</h2>
        <p class="mt-1 text-gray-600">Назначьте старших на позиции заводского графика</p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="С"
          end-placeholder="По"
          :clearable="false"
          style="width: 280px"
        />
        <el-button :icon="'Refresh'" :loading="loading" @click="load">Обновить</el-button>
      </div>
    </div>

    <el-card v-loading="loading">
      <el-table
        :data="days"
        row-key="shiftDate"
        :row-class-name="rowClassName"
        stripe
        style="width: 100%"
        empty-text="Нет данных за выбранный период"
      >
        <el-table-column label="Дата" width="150">
          <template #default="{ row }">
            <span class="font-medium">{{ formatDate(row.shiftDate) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="Смены и старшие" min-width="560">
          <template #default="{ row }">
            <div class="flex flex-wrap gap-3 py-1">
              <el-button
                v-for="item in row.items"
                :key="item.position"
                class="shift-plan-item"
                @click="openItem(row, item)"
              >
                <span class="shift-plan-item-content">
                  <span class="font-semibold">Смена {{ item.position }}</span>
                  <span>{{ formatShiftWindow(item) }}</span>
                  <span :class="item.shiftLeadName ? 'text-gray-800' : 'text-gray-500'">
                    {{ item.shiftLeadName || 'Не назначен' }}
                  </span>
                </span>
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <ShiftPlanItemDialog
      v-if="selectedDay && selectedItem"
      :model-value="true"
      :day="selectedDay"
      :item="selectedItem"
      :candidates="candidates"
      @update:model-value="closeDialog"
      @saved="onSaved"
      @reload="load"
    />
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
import ShiftPlanItemDialog from '@/components/planning/ShiftPlanItemDialog.vue'
import { shiftPlansApi } from '@/api/shiftPlans'
import { apiErrorMessage } from '@/utils/apiError'

dayjs.extend(utc)
dayjs.extend(timezone)

const from = ref(dayjs().subtract(7, 'day').format('YYYY-MM-DD'))
const to = ref(dayjs().add(14, 'day').format('YYYY-MM-DD'))
const days = ref([])
const candidates = ref([])
const loading = ref(false)
const selectedDay = ref(null)
const selectedItem = ref(null)
let loadSeq = 0

const dateRange = computed({
  get: () => [from.value, to.value],
  set: value => {
    if (!Array.isArray(value) || value.length !== 2 || !value[0] || !value[1]) return
    from.value = value[0]
    to.value = value[1]
  }
})

watch(() => [from.value, to.value], () => load())

onMounted(load)

async function load() {
  const seq = ++loadSeq
  loading.value = true
  try {
    const [daysResponse, candidatesResponse] = await Promise.all([
      shiftPlansApi.getRange(from.value, to.value),
      shiftPlansApi.getLeadCandidates()
    ])
    if (seq !== loadSeq) return
    days.value = daysResponse.data ?? []
    candidates.value = candidatesResponse.data ?? []
    if (selectedDay.value && selectedItem.value) {
      const shiftDate = selectedDay.value.shiftDate
      const position = selectedItem.value.position
      const freshDay = days.value.find(day => day.shiftDate === shiftDate)
      const freshItem = freshDay?.items?.find(item => item.position === position)
      if (freshDay && freshItem) {
        selectedDay.value = freshDay
        selectedItem.value = freshItem
      } else {
        closeDialog()
      }
    }
  } catch (error) {
    if (seq === loadSeq) {
      ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить план смен'))
    }
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

function formatDate(value) {
  return dayjs(value).format('DD.MM.YYYY')
}

function formatShiftWindow(item) {
  const start = dayjs(item.startAtUtc).tz(item.timeZoneId).format('HH:mm')
  const end = dayjs(item.endAtUtc).tz(item.timeZoneId).format('HH:mm')
  return `${start}–${end}`
}

function rowClassName({ row }) {
  const classes = ['shift-plan-row']
  if (row.isFactoryToday) classes.push('shift-current-date')
  else if (row.items?.length > 0 && row.items.every(item => item.state === 'Historical')) {
    classes.push('shift-past')
  } else classes.push('shift-future')
  return classes.join(' ')
}

function openItem(day, item) {
  selectedDay.value = day
  selectedItem.value = item
}

function closeDialog() {
  selectedDay.value = null
  selectedItem.value = null
}

function onSaved(savedDay, context) {
  const date = context?.date ?? savedDay.shiftDate
  const position = context?.position ?? selectedItem.value?.position
  if (!selectedDay.value || !selectedItem.value ||
    selectedDay.value.shiftDate !== date || selectedItem.value.position !== position ||
    savedDay.shiftDate !== date) return

  const index = days.value.findIndex(day => day.shiftDate === date)
  if (index !== -1) days.value[index] = savedDay
  selectedDay.value = savedDay
  selectedItem.value = savedDay.items?.find(item => item.position === position) ?? null
}
</script>

<style scoped>
.shift-plan-item {
  height: auto;
  min-width: 150px;
  margin: 0;
  padding: 10px 14px;
}

.shift-plan-item-content {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  white-space: normal;
}

:deep(.shift-past) {
  color: #6b7280;
  background-color: #f3f4f6;
}

:deep(.shift-current-date) {
  background-color: #eff6ff;
  box-shadow: inset 3px 0 #2563eb;
}

:deep(.shift-future) {
  color: #4b5563;
}
</style>
