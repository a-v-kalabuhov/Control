<template>
  <div class="qr-scanner-container">
    <!-- Видео с камеры -->
    <video 
      ref="videoRef" 
      class="w-full h-64 object-cover rounded-lg"
      autoplay
      muted
      playsinline
    ></video>

    <!-- Рамка сканирования -->
    <div class="scanner-frame">
      <div class="corner top-left"></div>
      <div class="corner top-right"></div>
      <div class="corner bottom-left"></div>
      <div class="corner bottom-right"></div>
    </div>

    <!-- Сообщение о результате -->
    <div v-if="scanResult" class="scan-result mt-4 p-3 bg-green-50 rounded-lg">
      <div class="flex items-center gap-2 text-green-700">
        <el-icon><CircleCheck /></el-icon>
        <span class="font-medium">QR распознан!</span>
      </div>
      <div class="text-sm text-gray-600 mt-1 font-mono">{{ scanResult }}</div>
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
        type="info" 
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

const emit = defineEmits(['confirm', 'cancel', 'error'])

const videoRef = ref(null)
const scanResult = ref(null)
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
    ElMessage.error('Ошибка запуска сканера: ' + error.message)
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

const onScanSuccess = (decodedText) => {
  scanResult.value = decodedText
  
  // Вибрация при успешном сканировании (если поддерживается)
  if (navigator.vibrate) {
    navigator.vibrate(200)
  }
  
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

.scanner-frame {
  @apply absolute top-0 left-0 right-0 mx-auto w-64 h-64 pointer-events-none;
}

.corner {
  @apply absolute w-8 h-8 border-4 border-primary-500;
}

.top-left {
  @apply top-0 left-0 border-r-0 border-b-0 rounded-tl-lg;
}

.top-right {
  @apply top-0 right-0 border-l-0 border-b-0 rounded-tr-lg;
}

.bottom-left {
  @apply bottom-0 left-0 border-r-0 border-t-0 rounded-bl-lg;
}

.bottom-right {
  @apply bottom-0 right-0 border-l-0 border-t-0 rounded-br-lg;
}
</style>