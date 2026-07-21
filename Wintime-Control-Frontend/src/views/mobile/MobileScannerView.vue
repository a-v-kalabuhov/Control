<template>
  <div class="mobile-scanner-view">
    <h2 class="text-xl font-bold text-gray-800 mb-4">QR-сканер</h2>

    <el-card>
      <QrScanner
        @confirm="handleConfirm"
        @cancel="handleCancel"
        @error="handleError"
      />
    </el-card>

    <!-- История сканирований -->
    <el-card class="mt-4">
      <template #header>
        <span class="font-semibold">Последние сканирования</span>
      </template>
      <el-table :data="scanHistory" stripe style="width: 100%">
        <el-table-column prop="timestamp" label="Время" width="110">
          <template #default="{ row }">
            {{ formatDate(row.timestamp) }}
          </template>
        </el-table-column>
        <el-table-column prop="typeLabel" label="Тип" width="130">
          <template #default="{ row }">
            <el-tag size="small">{{ row.typeLabel }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="details" label="Наименование" />
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import QrScanner from '@/components/mobile/QrScanner.vue'
import { scannerErrorText } from '@/utils/scannerError'
import { resolveScan } from '@/utils/scanDescription'
import dayjs from 'dayjs'

const scanHistory = ref([])

const handleConfirm = async (qrData) => {
  const info = await resolveScan(qrData)

  // Нераспознанное (битый QR, чужой объект, архив) — не пишем в историю.
  if (!info.ok) {
    ElMessage.warning(info.text)
    return
  }

  scanHistory.value.unshift({
    timestamp: new Date(),
    typeLabel: info.typeLabel,
    details: info.details
  })

  // Оставляем только последние 10
  if (scanHistory.value.length > 10) {
    scanHistory.value = scanHistory.value.slice(0, 10)
  }

  ElMessage.success('Распознано: ' + info.text)
}

const handleCancel = () => {
  ElMessage.info('Сканирование отменено')
}

const handleError = (error) => {
  ElMessage.error('Ошибка сканера: ' + scannerErrorText(error))
}

const formatDate = (date) => {
  return dayjs(date).format('HH:mm:ss')
}
</script>

<style scoped>
.mobile-scanner-view {
  @apply p-4;
}
</style>