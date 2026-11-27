<template>
  <div>
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-2xl font-bold text-gray-800">Телеметрия</h2>
      <div class="flex gap-2">
        <el-button data-test="refresh" :loading="loading" :disabled="deleting" @click="loadStatistics">Обновить</el-button>
        <el-button data-test="open-cleanup" type="danger" :disabled="loading || controlsLocked" @click="openCleanup">Очистить</el-button>
      </div>
    </div>

    <el-alert v-if="statisticsError" :title="statisticsError" type="error" :closable="false" class="mb-4" />

    <el-card v-if="statistics" data-test="statistics-panel" v-loading="loading" class="mb-4">
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
        <div><div class="text-sm text-gray-500">Всего записей</div><div data-test="total-count" class="text-2xl font-semibold">{{ statistics.totalCount }}</div></div>
        <div><div class="text-sm text-gray-500">Данные таблицы без индексов</div><div data-test="table-size" class="text-2xl font-semibold">{{ formatBytes(statistics.tableSizeBytes) }}</div></div>
        <div><div class="text-sm text-gray-500">Суммарный объём индексов</div><div data-test="indexes-size" class="text-2xl font-semibold">{{ formatBytes(statistics.indexesSizeBytes) }}</div></div>
      </div>
      <p class="text-sm text-gray-500">После очистки физический размер таблицы и индексов может уменьшиться не сразу из-за особенностей PostgreSQL.</p>
    </el-card>

    <el-card v-if="statistics" class="mb-4">
      <template #header>Текущий календарный месяц</template>
      <div data-test="current-month">{{ formatMonth(statistics.currentMonth) }} (текущий): {{ statistics.currentMonth.count }}</div>
    </el-card>

    <el-card v-if="statistics" class="mb-4">
      <template #header>Последние четыре календарные недели</template>
      <div v-for="week in statistics.recentWeeks" :key="week.startDate" data-test="recent-week" class="py-1">
        {{ formatDateRange(week) }}<span v-if="week.isCurrent"> (текущая)</span>: {{ week.count }}
      </div>
    </el-card>

    <el-card v-if="statistics">
      <template #header>Три предыдущих полных месяца</template>
      <div v-for="month in statistics.previousMonths" :key="month.startDate" data-test="previous-month" class="py-1">
        {{ formatMonth(month) }}: {{ month.count }}
      </div>
    </el-card>

    <el-dialog v-model="cleanupDialogVisible" title="Очистка телеметрии" width="480px"
      :before-close="beforeClose" :close-on-click-modal="!controlsLocked" :close-on-press-escape="!controlsLocked">
      <el-form label-position="top">
        <el-form-item label="Удалить записи строго раньше начала даты">
          <el-date-picker v-model="cleanupDate" data-test="cleanup-date" type="date" value-format="YYYY-MM-DD"
            :clearable="false" :disabled="controlsLocked" :disabled-date="isFutureDate" style="width: 100%" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="controlsLocked" @click="closeCleanup">Отмена</el-button>
        <el-button data-test="run-preview" type="primary" :loading="previewLoading" :disabled="deleting" @click="runPreview">Рассчитать</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="confirmationVisible" title="Подтверждение очистки" width="480px" append-to-body
      :before-close="beforeClose" :close-on-click-modal="!controlsLocked" :close-on-press-escape="!controlsLocked">
      <p>Дата: {{ cleanupDate }}</p>
      <p data-test="confirmation-text">{{ preview?.count ?? 0 }} записей будет удалено. Подтвердите удаление</p>
      <template #footer>
        <el-button :disabled="controlsLocked" @click="cancelConfirmation">Отмена</el-button>
        <el-button data-test="confirm-cleanup" type="danger" :loading="deleting" :disabled="!canConfirmCleanup" @click="confirmCleanup">Удалить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { telemetryAdminApi } from '@/api/telemetryAdmin'
import { apiErrorMessage } from '@/utils/apiError'
import { formatBytes, formatDateRange, formatMonth } from '@/utils/telemetryAdmin'

const statistics = ref(null)
const statisticsError = ref('')
const loading = ref(false)
const previewLoading = ref(false)
const deleting = ref(false)
const cleanupDialogVisible = ref(false)
const confirmationVisible = ref(false)
const cleanupDate = ref('')
const preview = ref(null)
const controlsLocked = computed(() => previewLoading.value || deleting.value)
const canConfirmCleanup = computed(() => !deleting.value && (preview.value?.count ?? 0) > 0)
const timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone

function errorText(error, fallback) {
  const message = apiErrorMessage(error, fallback)
  return typeof message === 'string' && message ? message : fallback
}

async function loadStatistics() {
  if (loading.value || deleting.value) return
  loading.value = true
  try {
    const response = await telemetryAdminApi.getStatistics(timeZone())
    statistics.value = response.data
    statisticsError.value = ''
  } catch (error) {
    statisticsError.value = errorText(error, 'Не удалось загрузить статистику телеметрии')
    ElMessage.error(statisticsError.value)
  } finally {
    loading.value = false
  }
}

function openCleanup() {
  if (loading.value || controlsLocked.value) return
  cleanupDate.value = ''
  preview.value = null
  confirmationVisible.value = false
  cleanupDialogVisible.value = true
}

function isFutureDate(date) {
  return dayjs(date).isAfter(dayjs(), 'day')
}

async function runPreview() {
  if (controlsLocked.value) return
  if (!cleanupDate.value) {
    ElMessage.warning('Выберите дату очистки')
    return
  }
  if (dayjs(cleanupDate.value).isAfter(dayjs(), 'day')) {
    ElMessage.warning('Дата очистки не может быть в будущем')
    return
  }
  previewLoading.value = true
  try {
    const response = await telemetryAdminApi.previewCleanup(cleanupDate.value, timeZone())
    preview.value = response.data
    confirmationVisible.value = true
  } catch (error) {
    confirmationVisible.value = false
    ElMessage.error(errorText(error, 'Не удалось рассчитать количество записей'))
  } finally {
    previewLoading.value = false
  }
}

function closeCleanup() {
  if (!controlsLocked.value) cleanupDialogVisible.value = false
}

function cancelConfirmation() {
  if (!controlsLocked.value) confirmationVisible.value = false
}

function beforeClose(done) {
  if (!controlsLocked.value) done()
}

async function confirmCleanup() {
  if (!canConfirmCleanup.value) return
  deleting.value = true
  let result
  try {
    const response = await telemetryAdminApi.cleanup(cleanupDate.value, timeZone())
    result = response.data
  } catch (error) {
    const isTimeout = error.response?.status === 504 || ['ECONNABORTED', 'ETIMEDOUT'].includes(error.code)
    ElMessage.error(isTimeout
      ? 'Очистка превысила допустимое время. Повторите операцию или обратитесь к администратору БД'
      : errorText(error, 'Ошибка очистки телеметрии'))
    return
  } finally {
    deleting.value = false
  }

  ElMessage.success(`Удалено записей: ${result.deletedCount}`)
  confirmationVisible.value = false
  cleanupDialogVisible.value = false
  preview.value = null
  await loadStatistics()
}

onMounted(loadStatistics)
</script>
