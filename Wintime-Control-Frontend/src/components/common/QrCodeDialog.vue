<template>
  <el-dialog
    :model-value="modelValue"
    title="QR-код"
    width="360px"
    align-center
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <div class="flex flex-col items-center gap-3">
      <div v-if="qrSvg" class="qr-box" v-html="qrSvg" />
      <div v-else class="qr-box flex items-center justify-center text-gray-400">
        Генерация…
      </div>
      <div class="text-base font-semibold text-center">{{ label }}</div>
    </div>

    <template #footer>
      <el-button @click="$emit('update:modelValue', false)">Закрыть</el-button>
      <el-button type="primary" :disabled="!qrSvg" @click="print">
        Печать
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch } from 'vue'
import QRCode from 'qrcode'
import { ElMessage } from 'element-plus'
import { buildQrPrintHtml } from '@/utils/qrPrint'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  qrData: { type: String, default: '' },
  label: { type: String, default: '' }
})
defineEmits(['update:modelValue'])

// SVG-разметка, сгенерированная qrcode из props.qrData (доверенный источник — не user input).
const qrSvg = ref('')

const render = async () => {
  qrSvg.value = ''
  if (!props.qrData) return
  try {
    qrSvg.value = await QRCode.toString(props.qrData, {
      type: 'svg',
      errorCorrectionLevel: 'H',
      margin: 2
    })
  } catch {
    ElMessage.error('Не удалось сгенерировать QR-код')
  }
}

watch(
  () => [props.modelValue, props.qrData],
  ([visible]) => { if (visible) render() }
)

const print = () => {
  const win = window.open('', '_blank', 'width=480,height=600')
  if (!win) {
    ElMessage.warning('Разрешите всплывающие окна для печати')
    return
  }
  win.document.write(buildQrPrintHtml(qrSvg.value, props.label))
  win.document.close()
}
</script>

<style scoped>
.qr-box {
  width: 16rem;
  height: 16rem;
}
.qr-box :deep(svg) {
  width: 100%;
  height: 100%;
}
</style>
