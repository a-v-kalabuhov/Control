<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Диспетчерская</h2>
        <p class="text-gray-600 mt-1">Управление сменно-суточными заданиями</p>
      </div>
      <el-button type="primary" @click="showCreateModal" v-if="canCreate">
        <el-icon class="mr-1"><Plus /></el-icon>
        Новое задание
      </el-button>
    </div>

    <!-- Статистика -->
    <div class="grid grid-cols-1 md:grid-cols-5 gap-4 mb-6">
      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-blue-100 rounded-lg">
            <el-icon class="text-blue-600 text-xl"><Document /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Всего</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.tasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-gray-100 rounded-lg">
            <el-icon class="text-gray-600 text-xl"><Edit /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Черновики</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.draftTasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-yellow-100 rounded-lg">
            <el-icon class="text-yellow-600 text-xl"><Clock /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">В работе</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.inProgressTasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-red-100 rounded-lg">
            <el-icon class="text-red-600 text-xl"><Warning /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Просрочено</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.overdueTasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-green-100 rounded-lg">
            <el-icon class="text-green-600 text-xl"><TrendCharts /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Выполнение</p>
            <template v-if="shiftProgress.current !== null">
              <p class="text-2xl font-bold text-green-600">{{ shiftProgress.current }}%</p>
            </template>
            <template v-else>
              <p class="text-2xl font-bold text-gray-400">нет заданий</p>
              <p v-if="shiftProgress.prev !== null" class="text-xs text-gray-400 mt-0.5">
                Пред. смена: {{ shiftProgress.prev }}%
              </p>
            </template>
          </div>
        </div>
      </div>
    </div>

    <!-- Фильтры -->
    <TaskFilters 
      :filters="tasksStore.filters"
      @update:filters="(filters) => tasksStore.setFilter(Object.keys(filters)[0], filters[Object.keys(filters)[0]])"
      @apply="loadTasks"
    />

    <!-- Таблица заданий -->
    <el-card>
      <el-table
        :data="sortedTasks"
        stripe
        style="width: 100%"
        v-loading="tasksStore.loading"
        @row-click="openDetail"
        class="cursor-pointer"
      >
        <el-table-column prop="immName" width="150">
          <template #header>
            <span class="col-header" @click="handleSort('immName')">
              ТПА <span class="sort-icon">{{ sortIcon('immName') }}</span>
            </span>
          </template>
        </el-table-column>

        <el-table-column prop="moldName" min-width="200">
          <template #header>
            <span class="col-header" @click="handleSort('moldName')">
              Пресс-форма <span class="sort-icon">{{ sortIcon('moldName') }}</span>
            </span>
          </template>
        </el-table-column>

        <el-table-column prop="personnelName" width="180">
          <template #header>
            <span class="col-header" @click="handleSort('personnelName')">
              Наладчик <span class="sort-icon">{{ sortIcon('personnelName') }}</span>
            </span>
          </template>
        </el-table-column>

        <el-table-column width="150">
          <template #header>
            <span class="col-header" @click="handleSort('planQuantity')">
              План / Факт <span class="sort-icon">{{ sortIcon('planQuantity') }}</span>
            </span>
          </template>
          <template #default="{ row }">
            <div class="text-sm">
              <div class="font-medium">{{ row.actualQuantity || 0 }} / {{ row.planQuantity }}</div>
              <div class="text-gray-500 text-xs">шт.</div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="Прогресс" width="180">
          <template #default="{ row }">
            <TaskProgress
              :plan-quantity="row.planQuantity"
              :actual-quantity="row.actualQuantity || 0"
            />
          </template>
        </el-table-column>

        <el-table-column prop="status" width="120">
          <template #header>
            <span class="col-header" @click="handleSort('status')">
              Статус <span class="sort-icon">{{ sortIcon('status') }}</span>
            </span>
          </template>
          <template #default="{ row }">
            <TaskStatusBadge :status="row.status" />
          </template>
        </el-table-column>

        <el-table-column prop="issuedAt" width="160">
          <template #header>
            <span class="col-header" @click="handleSort('issuedAt')">
              Выдано <span class="sort-icon">{{ sortIcon('issuedAt') }}</span>
            </span>
          </template>
          <template #default="{ row }">
            {{ formatDate(row.issuedAt) }}
          </template>
        </el-table-column>

        <el-table-column label="Действия" width="240" fixed="right">
          <template #default="{ row }">
            <el-button
              size="small"
              @click.stop="openDetail(row)"
            >
              Детали
            </el-button>
            <el-popconfirm
              v-if="canIssueTask(row)"
              :title="`${row.immName} / ${row.moldName}`"
              confirm-button-text="Выдать"
              cancel-button-text="Отмена"
              confirm-button-type="warning"
              width="260"
              @confirm="issueTask(row)"
            >
              <template #reference>
                <el-button
                  size="small"
                  type="warning"
                  @click.stop
                >
                  Выдать
                </el-button>
              </template>
            </el-popconfirm>
            <el-button
              v-if="canEditTask(row)"
              size="small"
              type="primary"
              @click.stop="editTask(row)"
            >
              Ред.
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Модальные окна -->
    <TaskFormModal
      v-model="formModalVisible"
      :task="editingTask"
      @success="loadTasks"
    />

    <TaskDetailModal
      v-model="detailModalVisible"
      :task="selectedTask"
      @edit="editTask"
      @issue="issueTask"
      @complete="completeTask"
      @close="closeTask"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useTasksStore } from '@/stores/tasks'
import { useAuthStore } from '@/stores/auth'
import { useDashboardStore } from '@/stores/dashboard'
import { immApi } from '@/api/imm'
import { moldsApi } from '@/api/molds'
import { personnelApi } from '@/api/personnel'
import TaskFilters from '@/components/tasks/TaskFilters.vue'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import TaskProgress from '@/components/tasks/TaskProgress.vue'
import TaskFormModal from './TaskFormModal.vue'
import TaskDetailModal from './TaskDetailModal.vue'
import dayjs from 'dayjs'

const tasksStore = useTasksStore()
const authStore = useAuthStore()
const dashboardStore = useDashboardStore()

const formModalVisible = ref(false)
const detailModalVisible = ref(false)
const editingTask = ref(null)
const selectedTask = ref(null)

const canCreate = computed(() => authStore.isManager || authStore.isAdmin)

// ── Выполнение по смене ────────────────────────────────────────────────────────

const SHIFT_STATUSES = new Set(['Issued', 'InProgress', 'Completed', 'Closed'])

function shiftBounds(shift, now) {
  const midnight = new Date(now)
  midnight.setHours(0, 0, 0, 0)
  const minutesNow = now.getHours() * 60 + now.getMinutes()
  const totalEnd = shift.startMinutes + shift.durationMinutes
  if (totalEnd <= 1440) {
    return {
      from: new Date(midnight.getTime() + shift.startMinutes * 60000),
      to:   new Date(midnight.getTime() + totalEnd * 60000)
    }
  }
  // смена переходит через полночь
  if (minutesNow >= shift.startMinutes) {
    return {
      from: new Date(midnight.getTime() + shift.startMinutes * 60000),
      to:   new Date(midnight.getTime() + totalEnd * 60000)
    }
  }
  const prevMidnight = new Date(midnight.getTime() - 86400000)
  return {
    from: new Date(prevMidnight.getTime() + shift.startMinutes * 60000),
    to:   new Date(midnight.getTime() + (totalEnd % 1440) * 60000)
  }
}

function prevShiftBounds(shift, now) {
  const midnight = new Date(now)
  midnight.setHours(0, 0, 0, 0)
  const endMinutes = (shift.startMinutes + shift.durationMinutes) % 1440
  const minutesNow = now.getHours() * 60 + now.getMinutes()
  const endMs = endMinutes <= minutesNow
    ? midnight.getTime() + endMinutes * 60000
    : midnight.getTime() - 86400000 + endMinutes * 60000
  return {
    from: new Date(endMs - shift.durationMinutes * 60000),
    to:   new Date(endMs)
  }
}

function avgProgress(tasks) {
  if (tasks.length === 0) return null
  const total = tasks.reduce((sum, t) => sum + (t.progressPercent || 0), 0)
  return Math.round(total / tasks.length)
}

function tasksInWindow(tasks, from, to) {
  return tasks.filter(t => {
    if (!SHIFT_STATUSES.has(t.status) || !t.issuedAt) return false
    const ts = new Date(t.issuedAt).getTime()
    return ts >= from.getTime() && ts < to.getTime()
  })
}

const shiftProgress = computed(() => {
  const now = new Date()
  const curr = dashboardStore.currentShift
  const prev = dashboardStore.lastCompletedShift

  if (!curr && !prev) return { current: null, prev: null }

  const currentPct = curr
    ? avgProgress(tasksInWindow(tasksStore.tasks, ...Object.values(shiftBounds(curr, now))))
    : null

  const prevPct = prev
    ? avgProgress(tasksInWindow(tasksStore.tasks, ...Object.values(prevShiftBounds(prev, now))))
    : null

  return { current: currentPct, prev: prevPct }
})

// ── Сортировка ────────────────────────────────────────────────────────────────

const SORT_COOKIE = 'tasks_sort_v1'

const readCookie = (name) => {
  const m = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'))
  return m ? decodeURIComponent(m[2]) : null
}

const writeCookie = (name, value) => {
  const exp = new Date(Date.now() + 365 * 864e5).toUTCString()
  document.cookie = `${name}=${encodeURIComponent(value)};expires=${exp};path=/`
}

const sort = ref({ field: 'issuedAt', dir: 'desc' })

const handleSort = (field) => {
  if (sort.value.field !== field) {
    sort.value = { field, dir: 'desc' }
  } else if (sort.value.dir === 'desc') {
    sort.value = { field, dir: 'asc' }
  } else {
    sort.value = { field: null, dir: null }
  }
  writeCookie(SORT_COOKIE, JSON.stringify(sort.value))
}

const sortIcon = (field) => {
  if (sort.value.field !== field) return ''
  return sort.value.dir === 'desc' ? '▼' : '▲'
}

const compareValues = (a, b) => {
  if (a == null && b == null) return 0
  if (a == null) return -1
  if (b == null) return 1
  const da = typeof a === 'string' ? Date.parse(a) : NaN
  const db = typeof b === 'string' ? Date.parse(b) : NaN
  if (!isNaN(da) && !isNaN(db)) return da - db
  if (typeof a === 'string') return a.localeCompare(b, 'ru')
  return a < b ? -1 : a > b ? 1 : 0
}

const sortedTasks = computed(() => {
  const tasks = tasksStore.filteredTasks
  if (!sort.value.field) return tasks
  const { field, dir } = sort.value
  const sign = dir === 'desc' ? -1 : 1
  return [...tasks].sort((a, b) => {
    const primary = compareValues(a[field], b[field]) * sign
    return primary !== 0 ? primary : compareValues(a.immName, b.immName)
  })
})

// ── Жизненный цикл ────────────────────────────────────────────────────────────

onMounted(() => {
  const saved = readCookie(SORT_COOKIE)
  if (saved) {
    try { sort.value = JSON.parse(saved) } catch {}
  }
  loadTasks()
  if (dashboardStore.shifts.length === 0) dashboardStore.loadShifts()
})

const loadTasks = async () => {
  await tasksStore.loadTasks()
}

const showCreateModal = () => {
  editingTask.value = null
  formModalVisible.value = true
}

const editTask = (task) => {
  editingTask.value = task
  formModalVisible.value = true
}

const openDetail = (task) => {
  selectedTask.value = task
  detailModalVisible.value = true
}

const completeTask = async (task) => {
  try {
    await ElMessageBox.prompt('Укажите фактическое количество (если отличается от плана)', 'Завершение задания', {
      inputPattern: /^\d+$/,
      inputErrorMessage: 'Введите число'
    })

    const { value } = await ElMessageBox.prompt('Причина (если план не выполнен)', 'Причина', {
      inputPattern: /.+/,
      inputErrorMessage: 'Введите причину'
    })

    await tasksStore.completeTask(task.id, {
      actualQuantity: parseInt(value),
      completionReason: value
    })
    
    detailModalVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка завершения задания')
    }
  }
}

const closeTask = async (task) => {
  try {
    await ElMessageBox.confirm('Закрыть задание?', 'Подтверждение', {
      type: 'warning'
    })

    await tasksStore.closeTask(task.id, {})
    detailModalVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка закрытия задания')
    }
  }
}

const canIssueTask = (task) => {
  return task.status === 'Draft' && canCreate.value
}

const issueTask = async (task) => {
  // Параллельно проверяем актуальность ТПА, пресс-формы и наладчика
  const checks = [
    immApi.getById(task.immId),
    moldsApi.getById(task.moldId),
    task.personnelId ? personnelApi.getById(task.personnelId) : Promise.resolve(null),
  ]

  let results
  try {
    results = await Promise.all(checks)
  } catch {
    ElMessage.error('Не удалось проверить актуальность данных задания')
    return
  }

  const [immRes, moldRes, personnelRes] = results
  const inactive = []
  if (!immRes.data.isActive)        inactive.push(`ТПА «${task.immName}»`)
  if (!moldRes.data.isActive)       inactive.push(`пресс-форма «${task.moldName}»`)
  if (personnelRes && !personnelRes.data.isActive) inactive.push(`наладчик «${task.personnelName}»`)

  if (inactive.length > 0) {
    ElMessage.warning(`Переведено в архив: ${inactive.join(', ')}. Отредактируйте задание перед выдачей.`)
    detailModalVisible.value = false
    editTask(task)
    return
  }

  await tasksStore.issueTask(task.id)
  detailModalVisible.value = false
}

const canEditTask = (task) => {
  return task.status === 'Draft' && canCreate.value
}

const formatDate = (date) => {
  if (!date) return '—'
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}

:deep(.el-table__row) {
  @apply cursor-pointer;
}

:deep(.el-table__row:hover) {
  @apply bg-blue-50;
}

.col-header {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  cursor: pointer;
  user-select: none;
  white-space: nowrap;
}

.col-header:hover {
  color: var(--el-color-primary);
}

.sort-icon {
  font-size: 10px;
  color: var(--el-color-primary);
  min-width: 10px;
}
</style>