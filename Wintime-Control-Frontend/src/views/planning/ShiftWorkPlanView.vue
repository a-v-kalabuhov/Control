<template>
  <div>
    <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">План работ на смену</h2>
        <p class="mt-1 text-gray-600">Назначьте наладчиков и выдайте задания в работу</p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <el-date-picker
          v-model="selectedDate"
          type="date"
          value-format="YYYY-MM-DD"
          :clearable="false"
          style="width: 150px"
        />
        <el-select v-model="selectedShiftNumber" style="width: 220px" placeholder="Выберите смену">
          <el-option
            v-for="shift in dashboardStore.shifts"
            :key="shift.number"
            :value="shift.number"
            :label="shiftLabel(shift)"
          />
        </el-select>
        <el-button :icon="'Refresh'" :loading="loading" @click="load">Обновить</el-button>
        <el-button
          type="warning"
          data-test="issue-all"
          :disabled="readyTasks.length === 0 || issuing || loading || hasPendingAssignments"
          :loading="issuing"
          @click="issueAll"
        >
          Выдать все готовые ({{ readyTasks.length }})
        </el-button>
      </div>
    </div>

    <el-alert
      v-if="!loading && dashboardStore.shifts.length === 0"
      type="warning"
      title="Не настроено расписание смен"
      :closable="false"
      class="mb-4"
    />

    <el-card v-loading="loading">
      <el-table :data="tasks" row-key="id" stripe style="width: 100%">
        <el-table-column label="Начало" width="90">
          <template #default="{ row }">{{ formatTime(row.plannedStartAt) }}</template>
        </el-table-column>
        <el-table-column prop="immName" label="ТПА" min-width="110" />
        <el-table-column prop="moldName" label="Пресс-форма" min-width="140" />
        <el-table-column label="Изделие" min-width="190">
          <template #default="{ row }">
            <div class="font-medium">{{ row.productFullArticle }}</div>
            <div class="text-sm text-gray-500">{{ row.productName }}</div>
          </template>
        </el-table-column>
        <el-table-column prop="planQuantity" label="План, шт." width="100" align="right" />
        <el-table-column label="Статус" width="120">
          <template #default="{ row }"><TaskStatusBadge :status="row.status" /></template>
        </el-table-column>
        <el-table-column label="Наладчик" min-width="190">
          <template #default="{ row }">
            <el-select
              v-if="canAssign(row)"
              :model-value="row.personnelId"
              :data-test="`personnel-${row.id}`"
              :clearable="row.status === 'Planned'"
              :disabled="!!assigning[row.id] || loading || issuing"
              :loading="!!assigning[row.id]"
              placeholder="Выберите наладчика"
              filterable
              style="width: 100%"
              @change="value => assignPersonnel(row, value)"
            >
              <el-option
                v-if="row.personnelId && !activeAdjusterIds.has(row.personnelId)"
                :value="row.personnelId"
                :label="`${row.personnelName} (архив)`"
                disabled
              />
              <el-option
                v-for="person in personnel"
                :key="person.id"
                :value="person.id"
                :label="person.fullName"
              />
            </el-select>
            <span v-else>{{ row.personnelName || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="Выдача" min-width="190">
          <template #default="{ row }">
            <el-button
              v-if="row.status === 'Planned'"
              type="warning"
              size="small"
              :data-test="`issue-${row.id}`"
              :disabled="!isReady(row) || issuing || loading || !!assigning[row.id]"
              @click="issueSingle(row)"
            >
              Выдать
            </el-button>
            <div v-if="issueErrors[row.id]" class="mt-1 text-xs text-red-600">
              {{ issueErrors[row.id] }}
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <span>{{ selectedShift ? 'На выбранную смену заданий нет' : 'Выберите смену' }}</span>
        </template>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import { tasksApi } from '@/api/tasks'
import { personnelApi } from '@/api/personnel'
import { useDashboardStore } from '@/stores/dashboard'
import { apiErrorMessage } from '@/utils/apiError'
import { archivedTaskRefs } from '@/utils/taskIssueCheck'
import { isReadyToIssue, resolveInitialShiftSelection, resolveShiftWindow, selectShiftWorkPlan } from '@/utils/shiftWorkPlan'

const dashboardStore = useDashboardStore()
const selectedDate = ref(dayjs().format('YYYY-MM-DD'))
const selectedShiftNumber = ref(null)
const tasks = ref([])
const personnel = ref([])
const loading = ref(false)
const issuing = ref(false)
const assigning = reactive({})
const issueErrors = reactive({})
let loadSeq = 0

const selectedShift = computed(() =>
  dashboardStore.shifts.find(s => s.number === selectedShiftNumber.value) ?? null)
const shiftWindow = computed(() => selectedShift.value
  ? resolveShiftWindow(selectedDate.value, selectedShift.value)
  : null)
const activeAdjusterIds = computed(() => new Set(personnel.value.map(p => p.id)))
const hasPendingAssignments = computed(() => Object.values(assigning).some(Boolean))
const readyTasks = computed(() => tasks.value.filter(t => isReadyToIssue(t, activeAdjusterIds.value)))

function isReady(task) {
  return isReadyToIssue(task, activeAdjusterIds.value)
}

function canAssign(task) {
  return task.status === 'Planned' || task.status === 'Issued'
}

function minutesLabel(minutes) {
  const value = minutes % 1440
  return `${String(Math.floor(value / 60)).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`
}

function shiftLabel(shift) {
  return `Смена ${shift.number} · ${minutesLabel(shift.startMinutes)}–${minutesLabel(shift.startMinutes + shift.durationMinutes)}`
}

function formatTime(value) {
  return value ? dayjs(value).format('HH:mm') : '—'
}

async function loadPersonnel() {
  try {
    const { data } = await personnelApi.getList({ isActive: true, role: 'Adjuster' })
    personnel.value = data ?? []
  } catch (error) {
    personnel.value = []
    ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить список наладчиков'))
  }
}

async function load() {
  if (!shiftWindow.value) return
  const currentWindow = shiftWindow.value
  const timeZone = selectedShift.value.timeZoneId ?? dayjs.tz.guess()
  const seq = ++loadSeq
  tasks.value = []
  loading.value = true
  try {
    const [plannedResponse, activeResponse] = await Promise.all([
      tasksApi.getList({
        plannedFrom: selectedDate.value,
        plannedTo: dayjs(currentWindow.end).tz(timeZone).format('YYYY-MM-DD')
      }),
      tasksApi.getList({
        activeFrom: currentWindow.start.toISOString(),
        activeTo: currentWindow.end.toISOString()
      })
    ])
    if (seq !== loadSeq) return
    tasks.value = selectShiftWorkPlan([
      ...(plannedResponse.data ?? []),
      ...(activeResponse.data ?? [])
    ], currentWindow)
  } catch (error) {
    if (seq === loadSeq) ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить план работ на смену'))
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

async function assignPersonnel(task, personnelId) {
  const normalizedId = personnelId || null
  assigning[task.id] = true
  try {
    await tasksApi.setPersonnel(task.id, normalizedId)
    task.personnelId = normalizedId
    task.personnelName = personnel.value.find(p => p.id === normalizedId)?.fullName ?? null
    delete issueErrors[task.id]
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Не удалось назначить наладчика'))
    await load()
  } finally {
    assigning[task.id] = false
  }
}

function issueErrorMessage(error) {
  return error?.response
    ? apiErrorMessage(error, 'Ошибка выдачи задания')
    : error?.message || 'Ошибка выдачи задания'
}

async function issueOne(task) {
  delete issueErrors[task.id]
  try {
    const inactive = await archivedTaskRefs(task)
    if (inactive.length > 0) {
      throw new Error(`Переведено в архив: ${inactive.join(', ')}. Отредактируйте задание перед выдачей.`)
    }
    await tasksApi.issue(task.id, task.personnelId)
    return true
  } catch (error) {
    issueErrors[task.id] = issueErrorMessage(error)
    return false
  }
}

async function issueSingle(task) {
  if (issuing.value || assigning[task.id]) return
  issuing.value = true
  try {
    if (await issueOne(task)) {
      ElMessage.success('Задание выдано в работу')
      await load()
    }
  } finally {
    issuing.value = false
  }
}

async function issueAll() {
  if (issuing.value || hasPendingAssignments.value || readyTasks.value.length === 0) return
  issuing.value = true
  let succeeded = 0
  let failed = 0
  try {
    // Последовательно: менеджер видит ошибку конкретного задания, остальные всё равно выдаются.
    for (const task of [...readyTasks.value]) {
      if (await issueOne(task)) succeeded++
      else failed++
    }
    await load()
    if (failed > 0) ElMessage.warning(`Выдано: ${succeeded}. Не выдано: ${failed}.`)
    else ElMessage.success(`Выдано заданий: ${succeeded}`)
  } finally {
    issuing.value = false
  }
}

watch(() => [selectedDate.value, selectedShiftNumber.value], (_, previous) => {
  // Первичную загрузку выполняет onMounted после получения расписания.
  if (previous[1] === null) return
  Object.keys(issueErrors).forEach(key => delete issueErrors[key])
  load()
})

onMounted(async () => {
  const personnelPromise = loadPersonnel()
  if (dashboardStore.shifts.length === 0) await dashboardStore.loadShifts()
  const initial = resolveInitialShiftSelection(dashboardStore.shifts)
  selectedDate.value = initial.date
  selectedShiftNumber.value = initial.shiftNumber
  await Promise.all([personnelPromise, load()])
})
</script>
