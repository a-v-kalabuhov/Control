<template>
  <div class="qr-scanner-container">
    <!-- Контейнер камеры: html5-qrcode сам вставляет сюда <video> и рамку qrbox -->
    <div id="qr-reader" class="w-full rounded-lg overflow-hidden"></div>

    <!-- Сообщение о результате -->
    <div v-if="scanResult" class="scan-result mt-4 p-3 bg-green-50 rounded-lg">
      <div class="flex items-center gap-2 text-green-700">
        <el-icon><CircleCheck /></el-icon>
        <span class="font-medium">QR распознан!</span>
      </div>
      <div class="text-base text-gray-800 mt-1 font-medium">{{ scanLabel }}</div>
    </div>

    <!-- Кнопки управления -->
    <div class="flex gap-3 mt-4">
      <el-button 
        type="primary" 
        size="large"
        class="flex-1 h-12"
        :disabled="!scanResult"
        @click="$emit('confirm', scanResult)"
      >
        Подтвердить
      </el-button>
      <el-button
        size="large"
        class="flex-1 h-12"
        @click="$emit('cancel')"
      >
        Отмена
      </el-button>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { Html5Qrcode } from 'html5-qrcode'
import { ElMessage } from 'element-plus'
import { scannerErrorText } from '@/utils/scannerError'
import { createDuplicateFilter } from '@/utils/duplicateFilter'
import { describeScan } from '@/utils/scanDescription'

const emit = defineEmits(['confirm', 'cancel', 'error'])

// Один и тот же QR не добавляем в историю чаще раза в 15 секунд.
const DUPLICATE_WINDOW_MS = 15000
const acceptScan = createDuplicateFilter(DUPLICATE_WINDOW_MS)

const scanResult = ref(null)  // сырой decodedText — уходит наружу через confirm
const scanLabel = ref('')     // человекочитаемое описание для показа пользователю
let html5QrCode = null

onMounted(async () => {
  await startScanner()
})

onUnmounted(() => {
  stopScanner()
})

const startScanner = async () => {
  try {
    html5QrCode = new Html5Qrcode('qr-reader')
    
    const config = {
      fps: 10,
      qrbox: { width: 250, height: 250 },
      aspectRatio: 1.0
    }

    await html5QrCode.start(
      { facingMode: 'environment' }, // Задняя камера
      config,
      onScanSuccess,
      onScanError
    )
  } catch (error) {
    ElMessage.error('Ошибка запуска сканера: ' + scannerErrorText(error))
    emit('error', error)
  }
}

const stopScanner = async () => {
  if (html5QrCode) {
    try {
      await html5QrCode.stop()
      html5QrCode.clear()
    } catch (error) {
      console.error('Ошибка остановки сканера:', error)
    }
  }
}

const onScanSuccess = async (decodedText) => {
  // Пропускаем повтор того же кода в пределах окна — камера сканирует непрерывно.
  if (!acceptScan(decodedText)) {
    return
  }

  scanResult.value = decodedText
  scanLabel.value = 'Загрузка…'

  // Вибрация при успешном сканировании (если поддерживается)
  if (navigator.vibrate) {
    navigator.vibrate(200)
  }

  // Наименование/артикул тянем из справочника по id (в QR их нет).
  scanLabel.value = await describeScan(decodedText)

  // Автоматически подтверждаем через 1 секунду
  setTimeout(() => {
    emit('confirm', decodedText)
  }, 1000)
}

const onScanError = (error) => {
  // Игнорируем ошибки сканирования (это нормально при отсутствии QR в кадре)
  if (error?.includes('No QR code found')) {
    return
  }
  console.warn('Scanner error:', error)
}
</script>

<style scoped>
.qr-scanner-container {
  @apply relative;
}
</style>