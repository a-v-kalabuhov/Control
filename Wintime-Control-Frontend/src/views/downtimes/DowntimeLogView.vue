<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Журнал простоев</h2>
        <p class="text-gray-600 mt-1">История остановок оборудования с разбивкой по причинам</p>
      </div>
    </div>

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
          <el-button type="primary" @click="loadEvents">Найти</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Таблица -->
    <el-card v-loading="loading">
      <el-table :data="events" stripe style="width: 100%" empty-text="Простоев не найдено">
        <el-table-column prop="immName" label="ТПА" width="160" />
        <el-table-column label="Начало" width="170">
          <template #default="{ row }">
            {{ formatDateTime(row.startTime) }}
          </template>
        </el-table-column>
        <el-table-column label="Конец" width="170">
          <template #default="{ row }">
            <span v-if="row.endTime">{{ formatDateTime(row.endTime) }}</span>
            <el-tag v-else type="warning" size="small">Активный</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="Длительность" width="130" align="center">
          <template #default="{ row }">
            {{ row.endTime ? formatDuration(row.durationSeconds) : '—' }}
          </template>
        </el-table-column>
        <el-table-column label="Причина" min-width="200">
          <template #default="{ row }">
            {{ row.reasonName || '—' }}
          </template>
        </el-table-column>
        <el-table-column label="Действия" width="160" fixed="right">
          <template #default="{ row }">
            <el-button size="small" @click="openEditDialog(row)">
              Изменить причину
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div v-if="events.length" class="mt-3 text-sm text-gray-500">
        Записей: {{ events.length }}
      </div>
    </el-card>

    <!-- Диалог изменения причины -->
    <el-dialog v-model="editDialog.visible" title="Изменить причину простоя" width="440px">
      <el-form label-width="80px">
        <el-form-item label="ТПА">
          <span class="font-medium">{{ editDialog.event?.immName }}</span>
        </el-form-item>
        <el-form-item label="Начало">
          <span>{{ formatDateTime(editDialog.event?.startTime) }}</span>
        </el-form-item>
        <el-form-item label="Причина">
          <el-select v-model="editDialog.reasonId" placeholder="Выберите причину" class="w-full">
            <el-option
              v-for="reason in reasons"
              :key="reason.id"
              :label="reason.name"
              :value="reason.id"
            >
              <span>{{ reason.name }}</span>
              <el-tag
                :type="reason.type === 'Planned' ? 'success' : 'danger'"
                size="small"
                class="ml-2"
              >
                {{ reason.type === 'Planned' ? 'Плановый' : 'Аварийный' }}
              </el-tag>
            </el-option>
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialog.visible = false">Отмена</el-button>
        <el-button type="primary" :loading="editDialog.saving" @click="saveReason">
          Сохранить
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { downtimeApi } from '@/api/downtime'
import { immApi } from '@/api/imm'

const loading = ref(false)
const events = ref([])
const imms = ref([])
const reasons = ref([])

const filters = reactive({
  dateRange: [dayjs().format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')],
  immId: null
})

const editDialog = reactive({
  visible: false,
  event: null,
  reasonId: null,
  saving: false
})

onMounted(async () => {
  await Promise.all([loadImms(), loadReasons(), loadEvents()])
})

const loadImms = async () => {
  try {
    const response = await immApi.getList({ isActive: true })
    imms.value = response.data
  } catch {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}

const loadReasons = async () => {
  try {
    const response = await downtimeApi.getReasons({ isActive: true })
    reasons.value = response.data
  } catch {
    ElMessage.error('Ошибка загрузки причин простоев')
  }
}

const loadEvents = async () => {
  loading.value = true
  try {
    const params = { eventType: 'Downtime' }
    if (filters.dateRange?.length === 2) {
      params.from = filters.dateRange[0]
      params.to = filters.dateRange[1] + 'T23:59:59'
    }
    if (filters.immId) params.immId = filters.immId

    const response = await downtimeApi.getEvents(params)
    events.value = response.data
  } catch {
    ElMessage.error('Ошибка загрузки журнала простоев')
  } finally {
    loading.value = false
  }
}

const openEditDialog = (event) => {
  editDialog.event = event
  editDialog.reasonId = event.reasonId ?? null
  editDialog.visible = true
}

const saveReason = async () => {
  if (!editDialog.reasonId) {
    ElMessage.warning('Выберите причину')
    return
  }
  editDialog.saving = true
  try {
    const response = await downtimeApi.updateEvent(editDialog.event.id, { reasonId: editDialog.reasonId })
    const idx = events.value.findIndex(e => e.id === editDialog.event.id)
    if (idx !== -1) events.value[idx] = response.data
    editDialog.visible = false
    ElMessage.success('Причина обновлена')
  } catch {
    ElMessage.error('Ошибка сохранения')
  } finally {
    editDialog.saving = false
  }
}

const formatDateTime = (dt) => {
  if (!dt) return '—'
  return dayjs(dt).format('DD.MM.YYYY HH:mm')
}

const formatDuration = (seconds) => {
  if (!seconds) return '0 мин'
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (h > 0) return `${h} ч ${m} мин`
  return `${m} мин`
}
</script>
