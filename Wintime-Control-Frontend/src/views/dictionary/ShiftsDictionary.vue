<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Расписание смен</h2>
        <p class="text-gray-600 mt-1">Шаблон рабочих смен цеха, повторяется ежедневно</p>
      </div>
      <el-button v-if="isAdmin" type="primary" @click="openEditDialog">
        <el-icon class="mr-1"><Edit /></el-icon>
        Редактировать
      </el-button>
    </div>

    <!-- Таблица смен (режим просмотра) -->
    <el-table :data="shifts" stripe v-loading="loading" style="width: 100%">
      <el-table-column prop="number" label="№" width="60" align="center" />
      <el-table-column label="Начало" width="100" align="center">
        <template #default="{ row }">
          <span class="font-medium">{{ row.startTime }}</span>
        </template>
      </el-table-column>
      <el-table-column label="Конец" width="100" align="center">
        <template #default="{ row }">
          <span class="font-medium">{{ row.endTime }}</span>
          <el-tag v-if="isNightShift(row)" type="info" size="small" class="ml-1">+1</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Длительность" width="140" align="center">
        <template #default="{ row }">
          {{ formatDuration(row.durationMinutes) }}
        </template>
      </el-table-column>
      <el-table-column label="Перерыв">
        <template #default="{ row }">
          <span v-if="row.breakDurationMinutes > 0">
            {{ row.breakStartTime }} – {{ row.breakEndTime }}
            <span class="text-gray-500">({{ formatDuration(row.breakDurationMinutes) }})</span>
          </span>
          <span v-else class="text-gray-400">—</span>
        </template>
      </el-table-column>
    </el-table>

    <!-- Диалог редактирования -->
    <el-dialog
      v-model="dialogVisible"
      title="Редактирование расписания смен"
      width="680px"
      :close-on-click-modal="false"
    >
      <div
        v-for="(shift, index) in editShifts"
        :key="shift._key"
        class="mb-4 p-4 border border-gray-200 rounded-lg"
      >
        <div class="flex items-center justify-between mb-3">
          <span class="font-semibold text-gray-700">Смена {{ index + 1 }}</span>
          <el-button type="danger" size="small" text @click="removeShift(index)">
            <el-icon><Delete /></el-icon>
            Удалить
          </el-button>
        </div>

        <el-row :gutter="12">
          <el-col :span="8">
            <div class="text-sm text-gray-500 mb-1">Начало смены</div>
            <el-time-picker
              v-model="shift.startTime"
              format="HH:mm"
              value-format="HH:mm"
              placeholder="08:00"
              style="width: 100%"
            />
          </el-col>
          <el-col :span="8">
            <div class="text-sm text-gray-500 mb-1">Длительность (ч)</div>
            <el-input-number
              v-model="shift.durationHours"
              :min="0.5"
              :max="23"
              :step="0.5"
              :precision="1"
              style="width: 100%"
            />
          </el-col>
          <el-col :span="8">
            <div class="text-sm text-gray-500 mb-1">Конец смены</div>
            <div class="h-9 flex items-center font-semibold text-gray-800">
              {{ computedEnd(shift) }}
              <el-tag v-if="isNightShiftEdit(shift)" type="info" size="small" class="ml-1">след. день</el-tag>
            </div>
          </el-col>
        </el-row>

        <el-row :gutter="12" class="mt-3">
          <el-col :span="8">
            <div class="text-sm text-gray-500 mb-1">Начало перерыва</div>
            <el-time-picker
              v-model="shift.breakStartTime"
              format="HH:mm"
              value-format="HH:mm"
              placeholder="12:00"
              style="width: 100%"
              :disabled="shift.breakDurationMinutes === 0"
            />
          </el-col>
          <el-col :span="8">
            <div class="text-sm text-gray-500 mb-1">Длительность перерыва (мин)</div>
            <el-input-number
              v-model="shift.breakDurationMinutes"
              :min="0"
              :max="240"
              :step="15"
              style="width: 100%"
            />
          </el-col>
          <el-col :span="8">
            <div class="text-sm text-gray-500 mb-1">Конец перерыва</div>
            <div class="h-9 flex items-center font-semibold text-gray-800">
              {{ shift.breakDurationMinutes > 0 ? computedBreakEnd(shift) : '—' }}
            </div>
          </el-col>
        </el-row>
      </div>

      <el-button @click="addShift" type="success" plain style="width: 100%" class="mt-2">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить смену
      </el-button>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveShifts" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { shiftsApi } from '@/api/shifts'
import { usePermissions } from '@/composables/usePermissions'

const { canAccess } = usePermissions()
const isAdmin = computed(() => canAccess(['Admin']))

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const shifts = ref([])
const editShifts = ref([])
let keyCounter = 0

onMounted(loadShifts)

async function loadShifts() {
  loading.value = true
  try {
    const response = await shiftsApi.getShifts()
    shifts.value = response.data
  } catch {
    ElMessage.error('Ошибка загрузки расписания смен')
  } finally {
    loading.value = false
  }
}

function formatDuration(minutes) {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return m === 0 ? `${h} ч` : `${h} ч ${m} мин`
}

function isNightShift(row) {
  return row.startMinutes + row.durationMinutes > 1440
}

function minutesToTime(totalMinutes) {
  const h = Math.floor(totalMinutes / 60) % 24
  const m = totalMinutes % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`
}

function timeToMinutes(time) {
  if (!time) return 0
  const [h, m] = time.split(':').map(Number)
  return h * 60 + m
}

function computedEnd(shift) {
  if (!shift.startTime) return '—'
  const endMin = timeToMinutes(shift.startTime) + Math.round(shift.durationHours * 60)
  return minutesToTime(endMin)
}

function computedBreakEnd(shift) {
  if (!shift.breakStartTime || !shift.breakDurationMinutes) return '—'
  return minutesToTime(timeToMinutes(shift.breakStartTime) + shift.breakDurationMinutes)
}

function isNightShiftEdit(shift) {
  if (!shift.startTime) return false
  return timeToMinutes(shift.startTime) + Math.round(shift.durationHours * 60) > 1440
}

function openEditDialog() {
  editShifts.value = shifts.value.map(s => ({
    _key: ++keyCounter,
    startTime: s.startTime,
    durationHours: s.durationMinutes / 60,
    breakStartTime: s.breakStartTime ?? '12:00',
    breakDurationMinutes: s.breakDurationMinutes
  }))
  dialogVisible.value = true
}

function addShift() {
  editShifts.value.push({
    _key: ++keyCounter,
    startTime: '08:00',
    durationHours: 9,
    breakStartTime: '12:00',
    breakDurationMinutes: 60
  })
}

async function removeShift(index) {
  if (editShifts.value.length === 1) {
    try {
      await ElMessageBox.confirm(
        'Нельзя оставить список смен пустым. Добавить смену по умолчанию (08:00–17:00)?',
        'Удаление последней смены',
        { type: 'warning', confirmButtonText: 'Добавить дефолтную', cancelButtonText: 'Отмена' }
      )
      editShifts.value.splice(index, 1)
      editShifts.value.push({
        _key: ++keyCounter,
        startTime: '08:00',
        durationHours: 9,
        breakStartTime: '12:00',
        breakDurationMinutes: 60
      })
    } catch {
      // пользователь отменил — ничего не делаем
    }
    return
  }
  editShifts.value.splice(index, 1)
}

async function saveShifts() {
  saving.value = true
  try {
    const payload = editShifts.value.map(s => ({
      startMinutes: timeToMinutes(s.startTime),
      durationMinutes: Math.round(s.durationHours * 60),
      breakStartMinutes: timeToMinutes(s.breakStartTime),
      breakDurationMinutes: s.breakDurationMinutes
    }))

    const response = await shiftsApi.saveShifts(payload)
    shifts.value = response.data
    dialogVisible.value = false
    ElMessage.success('Расписание смен сохранено')
  } catch (error) {
    const errors = error.response?.data?.errors
    if (errors?.length) {
      ElMessage.error(errors[0])
    } else {
      ElMessage.error('Ошибка сохранения расписания смен')
    }
  } finally {
    saving.value = false
  }
}
</script>
