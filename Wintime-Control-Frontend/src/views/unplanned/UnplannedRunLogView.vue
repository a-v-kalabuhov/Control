<template>
  <div class="p-4">
    <h2 class="text-xl font-semibold mb-4">Журнал работы без задания</h2>

    <el-table :data="runs" v-loading="loading" border>
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
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { unplannedRunsApi } from '@/api/unplannedRuns'
import { formatRunStatus } from './unplannedRunFormat'

const runs = ref([])
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
    const { data } = await unplannedRunsApi.getList({})
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

onMounted(load)
</script>
