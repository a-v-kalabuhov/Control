<template>
  <div class="p-4">
    <h2 class="text-xl font-semibold mb-4">Журнал работы без задания</h2>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="Период">
          <el-date-picker
            v-model="filters.dateRange"
            type="daterange"
            start-placeholder="С"
            end-placeholder="По"
            value-format="YYYY-MM-DD"
            class="w-72"
          />
        </el-form-item>
        <el-form-item label="ТПА">
          <el-select v-model="filters.immId" placeholder="Все машины" clearable class="w-48">
            <el-option
              v-for="imm in imms"
              :key="imm.id"
              :label="imm.name"
              :value="imm.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="load">Найти</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-table :data="runs" v-loading="loading" border empty-text="Эпизодов не найдено">
      <el-table-column label="ТПА" prop="immName" min-width="140" />
      <el-table-column label="Начало" min-width="160">
        <template #default="{ row }">{{ formatDt(row.startTime) }}</template>
      </el-table-column>
      <el-table-column label="Конец" min-width="160">
        <template #default="{ row }">{{ row.endTime ? formatDt(row.endTime) : '—' }}</template>
      </el-table-column>
      <el-table-column label="Циклов" prop="cycleCount" width="90" align="center" />
      <el-table-column label="Статус" min-width="200">
        <template #default="{ row }">{{ formatRunStatus(row) }}</template>
      </el-table-column>
      <el-table-column label="Наладчик" prop="personnelName" min-width="160">
        <template #default="{ row }">{{ row.personnelName ?? '—' }}</template>
      </el-table-column>
      <el-table-column label="Действие" width="160">
        <template #default="{ row }">
          <el-button size="small" type="primary" @click="openAssign(row)">Назначить</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="assignVisible" title="Назначить задание эпизоду" width="520px">
      <el-select v-model="selectedTaskId" placeholder="Выберите задание" class="w-full">
        <el-option
          v-for="c in candidates"
          :key="c.taskId"
          :value="c.taskId"
          :label="(c.recommended ? '★ ' : '') + c.label + (c.personnelName ? ` · ${c.personnelName}` : '')"
        />
      </el-select>
      <p v-if="recommendedLabel" class="text-sm text-gray-500 mt-2">
        Рекомендуется: примыкает к заданию «{{ recommendedLabel }}»
      </p>
      <template #footer>
        <el-button @click="assignVisible = false">Отмена</el-button>
        <el-button type="primary" :disabled="!selectedTaskId" @click="confirmAssign">Назначить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { unplannedRunsApi } from '@/api/unplannedRuns'
import { immApi } from '@/api/imm'
import { formatRunStatus } from './unplannedRunFormat'

const runs = ref([])
const imms = ref([])
const filters = reactive({
  dateRange: [dayjs().format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')],
  immId: null
})
const loading = ref(false)
const assignVisible = ref(false)
const candidates = ref([])
const selectedTaskId = ref(null)
const currentRun = ref(null)

const recommendedLabel = computed(() => candidates.value.find(c => c.recommended)?.label ?? '')

function formatDt(v) {
  return new Date(v).toLocaleString('ru-RU')
}

async function load() {
  loading.value = true
  try {
    const params = {}
    if (filters.dateRange?.length === 2) {
      params.from = filters.dateRange[0]
      params.to = filters.dateRange[1] + 'T23:59:59'
    }
    if (filters.immId) params.immId = filters.immId

    const { data } = await unplannedRunsApi.getList(params)
    runs.value = data
  } catch (e) {
    ElMessage.error('Не удалось загрузить журнал')
  } finally {
    loading.value = false
  }
}

async function openAssign(row) {
  currentRun.value = row
  selectedTaskId.value = null
  try {
    const { data } = await unplannedRunsApi.getCandidates(row.id)
    candidates.value = data
    selectedTaskId.value = data.find(c => c.recommended)?.taskId ?? null
    assignVisible.value = true
  } catch (e) {
    ElMessage.error('Не удалось загрузить кандидатов')
  }
}

async function confirmAssign() {
  try {
    await unplannedRunsApi.assign(currentRun.value.id, selectedTaskId.value)
    ElMessage.success('Задание назначено')
    assignVisible.value = false
    await load()
  } catch (e) {
    ElMessage.error(e.response?.data ?? 'Ошибка назначения')
  }
}

async function loadImms() {
  try {
    const { data } = await immApi.getList({ isActive: true })
    imms.value = data
  } catch (e) {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}

onMounted(() => Promise.all([loadImms(), load()]))
</script>
