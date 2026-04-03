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
        <el-table-column prop="timestamp" label="Время" width="160">
          <template #default="{ row }">
            {{ formatDate(row.timestamp) }}
          </template>
        </el-table-column>
        <el-table-column prop="entity" label="Тип" width="100">
          <template #default="{ row }">
            <el-tag size="small">{{ row.entity }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="id" label="ID" />
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import QrScanner from '@/components/mobile/QrScanner.vue'
import dayjs from 'dayjs'

const scanHistory = ref([])

const handleConfirm = (qrData) => {
  try {
    const parsed = JSON.parse(qrData)
    
    scanHistory.value.unshift({
      timestamp: new Date(),
      entity: parsed.entity,
      id: parsed.id,
      raw: qrData
    })

    // Оставляем только последние 10
    if (scanHistory.value.length > 10) {
      scanHistory.value = scanHistory.value.slice(0, 10)
    }

    ElMessage.success(`Распознано: ${parsed.entity} - ${parsed.id}`)
  } catch (error) {
    ElMessage.warning('Неверный формат QR-кода')
  }
}

const handleCancel = () => {
  ElMessage.info('Сканирование отменено')
}

const handleError = (error) => {
  ElMessage.error('Ошибка сканера: ' + error.message)
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