<template>
  <div>
    <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">План-график цеха</h2>
        <p class="text-gray-600 mt-1">{{ periodText }}</p>
      </div>
      <div class="flex items-center gap-2">
        <el-radio-group v-model="scale" size="small">
          <el-radio-button v-for="s in SCALES" :key="s" :value="s">{{ SCALE_LABELS[s] }}</el-radio-button>
        </el-radio-group>
        <el-button-group>
          <el-button size="small" data-test="prev" @click="step(-1)">←</el-button>
          <el-button size="small" data-test="today" @click="anchor = new Date()">Сегодня</el-button>
          <el-button size="small" data-test="next" @click="step(1)">→</el-button>
        </el-button-group>
      </div>
    </div>

    <el-card v-loading="loading && !board" class="overflow-x-auto">
      <GanttBoard
        v-if="board"
        :board="board"
        :from="period.from"
        :to="period.to"
        :now="now"
        :ticks="ticks"
        @select="openDetail"
      />
    </el-card>

    <TaskDetailModal
      v-model="detailVisible"
      :task="selectedTask"
      @edit="editTask"
      @issue="issueTask"
      @close="closeTask"
    />
    <TaskFormModal v-model="formVisible" :task="editingTask" @success="load" />
    <IssueTaskDialog v-model="issueVisible" :task="issuingTask" @issued="load" />
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { ElMessage } from 'element-plus'
import GanttBoard from '@/components/planning/GanttBoard.vue'
import TaskDetailModal from '@/views/tasks/TaskDetailModal.vue'
import TaskFormModal from '@/views/tasks/TaskFormModal.vue'
import IssueTaskDialog from '@/views/tasks/IssueTaskDialog.vue'
import { planningApi } from '@/api/planning'
import { useDashboardStore } from '@/stores/dashboard'
import { useTasksStore } from '@/stores/tasks'
import { apiErrorMessage } from '@/utils/apiError'
import { archivedTaskRefs } from '@/utils/taskIssueCheck'
import { SCALES, SCALE_LABELS, periodFor, stepAnchor, periodLabel, gridTicks } from '@/utils/planningBoard'

const REFRESH_MS = 60_000

const dashboardStore = useDashboardStore()
const tasksStore = useTasksStore()

const scale = ref('week')
const anchor = ref(new Date())
const now = ref(new Date())
const board = ref(null)
const loading = ref(false)

const firstShiftStart = computed(() =>
  dashboardStore.shifts.length ? Math.min(...dashboardStore.shifts.map(s => s.startMinutes)) : 0)
const period = computed(() => periodFor(scale.value, anchor.value, firstShiftStart.value))
const ticks = computed(() => gridTicks(scale.value, period.value.from, period.value.to, dashboardStore.shifts))
const periodText = computed(() => periodLabel(scale.value, period.value.from, period.value.to))

// Счётчик запросов: применяем только ответ последнего, устаревшие игнорируем
let loadSeq = 0
async function load() {
  const seq = ++loadSeq
  loading.value = true
  try {
    const { data } = await planningApi.getBoard({
      from: period.value.from.toISOString(),
      to: period.value.to.toISOString()
    })
    if (seq !== loadSeq) return
    board.value = data
    now.value = new Date(data.now)
  } catch (error) {
    if (seq !== loadSeq) return
    // Последняя загруженная доска остаётся на экране, таймер продолжает попытки
    ElMessage.error(apiErrorMessage(error, 'Не удалось загрузить план-график'))
  } finally {
    if (seq === loadSeq) loading.value = false
  }
}

function step(dir) {
  anchor.value = stepAnchor(scale.value, anchor.value, dir)
}

// Ключ-строка: новый массив в геттере срабатывал бы на каждый пересчёт period (например, загрузку смен)
watch(() => `${period.value.from.getTime()}|${period.value.to.getTime()}`, load)

let timer = null
onMounted(async () => {
  // Таймер — до первого await, иначе ранний unmount оставит его без владельца
  timer = setInterval(load, REFRESH_MS)
  if (dashboardStore.shifts.length === 0) await dashboardStore.loadShifts()
  await load()
})
onUnmounted(() => clearInterval(timer))

// ── Карточка задания: правка плана — только здесь (Гантт неинтерактивный) ──────
const detailVisible = ref(false)
const selectedTask = ref(null)
const formVisible = ref(false)
const editingTask = ref(null)
const issueVisible = ref(false)
const issuingTask = ref(null)

function openDetail(task) {
  selectedTask.value = task
  detailVisible.value = true
}

function editTask(task) {
  detailVisible.value = false
  editingTask.value = task
  formVisible.value = true
}

async function issueTask(task) {
  let inactive
  try {
    inactive = await archivedTaskRefs(task)
  } catch {
    ElMessage.error('Не удалось проверить актуальность данных задания')
    return
  }
  if (inactive.length > 0) {
    ElMessage.warning(`Переведено в архив: ${inactive.join(', ')}. Отредактируйте задание перед выдачей.`)
    editTask(task)
    return
  }
  detailVisible.value = false
  issuingTask.value = task
  issueVisible.value = true
}

async function closeTask(task, closeReason) {
  try {
    await tasksStore.closeTask(task.id, { closeReason })
    detailVisible.value = false
    await load()
  } catch {
    ElMessage.error('Ошибка закрытия задания')
  }
}
</script>
