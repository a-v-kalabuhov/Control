<template>
  <div class="min-h-[160px]">
    <div class="mb-4 flex items-start justify-between gap-3">
      <div>
        <el-button data-test="back-to-item" text class="mb-2 px-0" @click="backToItem">
          ← Назад к смене
        </el-button>
        <h2 class="text-xl font-bold text-gray-800">Сменный рапорт</h2>
        <p v-if="report" class="mt-1 text-sm text-gray-600">
          {{ formatDate(report.shiftDate) }} · {{ formatDateTime(report.startAtUtc) }} —
          {{ formatDateTime(report.endAtUtc) }}
        </p>
        <p v-if="report?.shiftLeadName" class="mt-1 text-sm text-gray-500">
          Старший: {{ report.shiftLeadName }}
        </p>
      </div>
      <el-button
        data-test="refresh-report"
        :loading="loading"
        :disabled="saving"
        @click="refresh"
      >
        Обновить
      </el-button>
    </div>

    <ShiftClosureCards
      :report="report"
      :loading="loading"
      :saving="saving"
      :field-errors="fieldErrors"
      :is-dirty="isDirty"
      @update-manual="updateManual"
      @save="saveDraft"
      @close="requestClose(false)"
    />

    <el-dialog
      v-model="earlyCloseDialog"
      title="Закрыть смену досрочно?"
      width="480px"
      :teleported="false"
    >
      <span>Смена ещё не закончилась. Закрыть сменный рапорт досрочно?</span>
      <template #footer>
        <el-button data-action="cancel-early-close" @click="earlyCloseDialog = false">Отмена</el-button>
        <el-button type="warning" data-action="confirm-early-close" @click="confirmEarlyClose">
          Закрыть досрочно
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import ShiftClosureCards from '@/components/shift-closures/ShiftClosureCards.vue'
import { useShiftClosureEditor } from '@/composables/useShiftClosureEditor'
import { useShiftStaffingRefresh } from '@/composables/useShiftStaffingRefresh'

const route = useRoute()
const router = useRouter()
const itemId = computed(() => String(route.params.itemId))
const {
  report, loading, saving, fieldErrors, isDirty,
  load, updateManual, save, close
} = useShiftClosureEditor(itemId.value)
const earlyCloseDialog = ref(false)

const { refresh } = useShiftStaffingRefresh({
  getEndAtUtc: () => report.value?.endAtUtc,
  // load() сам пропускает фоновое обновление при dirty; ручной ввод не затирается.
  refresh: () => load()
})

onMounted(refresh)

function formatDate(value) {
  return value ? dayjs(value).format('DD.MM.YYYY') : '—'
}

function formatDateTime(value) {
  return value ? dayjs(value).format('DD.MM.YYYY HH:mm') : '—'
}

function backToItem() {
  router.push(`/shift-lead/items/${itemId.value}`)
}

async function saveDraft() {
  if (await save()) ElMessage.success('Сменный рапорт сохранён')
}

async function requestClose(confirmEarlyClose) {
  const result = await close({ confirmEarlyClose })
  if (result.earlyCloseConfirmationRequired) earlyCloseDialog.value = true
  if (result.success) {
    earlyCloseDialog.value = false
    ElMessage.success('Смена закрыта')
  }
}

async function confirmEarlyClose() {
  earlyCloseDialog.value = false
  await requestClose(true)
}
</script>
