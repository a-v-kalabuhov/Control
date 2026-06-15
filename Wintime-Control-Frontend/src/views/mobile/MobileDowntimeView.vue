<template>
  <div class="p-4">
    <!-- Кнопка регистрации простоя -->
    <el-button
      type="warning"
      size="large"
      class="w-full h-14 text-lg mb-4"
      @click="openStartDialog"
    >
      <el-icon class="mr-2"><Plus /></el-icon>
      Зарегистрировать простой
    </el-button>

    <!-- Список простоев -->
    <div v-if="loading" class="flex justify-center py-8">
      <el-icon class="text-3xl animate-spin text-primary-500"><Loading /></el-icon>
    </div>

    <div v-else-if="events.length === 0" class="text-center py-12 text-gray-400">
      <el-icon class="text-5xl mb-3"><Clock /></el-icon>
      <p class="text-base">Простоев за сегодня нет</p>
    </div>

    <div v-else class="space-y-3">
      <div
        v-for="event in events"
        :key="event.id"
        class="rounded-xl border-2 p-4"
        :class="event.endTime ? 'bg-white border-gray-200' : 'bg-orange-50 border-orange-300'"
      >
        <!-- Статус -->
        <div class="flex items-center justify-between mb-2">
          <span class="font-bold text-base">{{ event.immName }}</span>
          <el-tag :type="event.endTime ? 'info' : 'warning'" size="large">
            {{ event.endTime ? 'Завершён' : 'Активный' }}
          </el-tag>
        </div>

        <!-- Причина -->
        <div class="text-gray-700 mb-2">
          {{ event.reasonName || 'Причина не указана' }}
        </div>

        <!-- Времена -->
        <div class="text-sm text-gray-500 mb-3">
          <span>Начало: {{ formatTime(event.startTime) }}</span>
          <span v-if="event.endTime" class="ml-3">
            Конец: {{ formatTime(event.endTime) }} —
            <strong>{{ formatDuration(event.durationSeconds) }}</strong>
          </span>
          <span v-else class="ml-3 text-orange-600 font-medium">В процессе...</span>
        </div>

        <!-- Кнопка завершения для активного -->
        <el-button
          v-if="!event.endTime"
          type="danger"
          size="large"
          class="w-full"
          :loading="stoppingId === event.id"
          @click="stopDowntime(event)"
        >
          Завершить простой
        </el-button>
      </div>
    </div>

    <!-- Диалог начала простоя -->
    <el-dialog
      v-model="startDialog.visible"
      title="Регистрация простоя"
      width="95%"
    >
      <el-form label-width="80px" class="mt-2">
        <el-form-item label="ТПА">
          <el-select
            v-model="startDialog.immId"
            placeholder="Выберите машину"
            class="w-full"
            filterable
          >
            <el-option
              v-for="imm in imms"
              :key="imm.id"
              :label="imm.name"
              :value="imm.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Причина">
          <el-select
            v-model="startDialog.reasonId"
            placeholder="Выберите причину"
            class="w-full"
            filterable
          >
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
        <el-button size="large" @click="startDialog.visible = false">Отмена</el-button>
        <el-button
          type="warning"
          size="large"
          :loading="startDialog.saving"
          @click="confirmStart"
        >
          Начать простой
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
import { useMobileStore } from '@/stores/mobile'

const mobileStore = useMobileStore()

const loading = ref(false)
const stoppingId = ref(null)
const events = ref([])
const imms = ref([])
const reasons = ref([])

const startDialog = reactive({
  visible: false,
  immId: null,
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
    await mobileStore.loadDowntimeReasons()
    reasons.value = mobileStore.downtimeReasons
  } catch {
    ElMessage.error('Ошибка загрузки причин')
  }
}

const loadEvents = async () => {
  loading.value = true
  try {
    const todayStart = dayjs().startOf('day').toISOString()
    const response = await downtimeApi.getEvents({
      from: todayStart,
      eventType: 'Downtime'
    })
    events.value = response.data
  } catch {
    ElMessage.error('Ошибка загрузки простоев')
  } finally {
    loading.value = false
  }
}

const openStartDialog = () => {
  startDialog.immId = null
  startDialog.reasonId = null
  startDialog.visible = true
}

const confirmStart = async () => {
  if (!startDialog.immId) {
    ElMessage.warning('Выберите машину')
    return
  }
  if (!startDialog.reasonId) {
    ElMessage.warning('Выберите причину')
    return
  }
  startDialog.saving = true
  try {
    const result = await mobileStore.startDowntime(startDialog.immId, startDialog.reasonId)
    if (result.success) {
      startDialog.visible = false
      await loadEvents()
    }
  } finally {
    startDialog.saving = false
  }
}

const stopDowntime = async (event) => {
  stoppingId.value = event.id
  try {
    const result = await mobileStore.stopDowntime(event.immId)
    if (result.success) {
      await loadEvents()
    }
  } finally {
    stoppingId.value = null
  }
}

const formatTime = (dt) => {
  if (!dt) return '—'
  return dayjs(dt).format('HH:mm')
}

const formatDuration = (seconds) => {
  if (!seconds) return '0 мин'
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (h > 0) return `${h} ч ${m} мин`
  return `${m} мин`
}
</script>
