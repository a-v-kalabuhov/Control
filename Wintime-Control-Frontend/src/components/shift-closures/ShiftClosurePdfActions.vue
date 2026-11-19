<template>
  <div class="flex flex-wrap gap-2">
    <el-button
      v-if="!closed"
      data-test="print-blank"
      :loading="downloading === 'blank'"
      :disabled="!itemId || downloading !== null"
      @click="downloadBlank"
    >
      Печать бланка
    </el-button>
    <el-button
      v-else-if="closureId"
      data-test="print-closed"
      :loading="downloading === 'closed'"
      :disabled="downloading !== null"
      @click="downloadClosed"
    >
      Печать рапорта
    </el-button>
  </div>
</template>

<script setup>
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import * as shiftClosuresApi from '@/api/shiftClosures'
import { apiErrorMessage } from '@/utils/apiError'
import { downloadBlob } from '@/utils/downloadBlob'

defineOptions({ name: 'ShiftClosurePdfActions' })

const props = defineProps({
  itemId: { type: String, default: null },
  closureId: { type: String, default: null },
  state: { type: String, default: 'Draft' },
  shiftDate: { type: String, default: '' }
})

const downloading = ref(null)
const closed = computed(() => props.state === 'Closed')

async function downloadBlank() {
  if (!props.itemId || closed.value || downloading.value) return
  await download('blank', () => shiftClosuresApi.getBlankPdf(props.itemId),
    `shift-closure-blank-${props.shiftDate || props.itemId}.pdf`)
}

async function downloadClosed() {
  if (!props.closureId || !closed.value || downloading.value) return
  await download('closed', () => shiftClosuresApi.getClosedPdf(props.closureId),
    `shift-closure-${props.shiftDate || props.closureId}.pdf`)
}

async function download(kind, request, filename) {
  downloading.value = kind
  try {
    const response = await request()
    downloadBlob(response.data, filename)
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Не удалось скачать PDF сменного рапорта'))
  } finally {
    downloading.value = null
  }
}
</script>
