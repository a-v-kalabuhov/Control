<template>
  <div v-loading="loading" class="shift-closure">
    <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
      <div>
        <span v-if="report?.state === 'Closed'" class="font-medium text-gray-700">Рапорт закрыт</span>
        <span v-else class="text-gray-600">Черновик сменного рапорта</span>
        <span v-if="isDirty" class="ml-2 text-sm text-amber-600">Есть несохранённые изменения</span>
      </div>
      <div class="flex gap-2">
        <el-button
          type="primary"
          data-action="save"
          :disabled="closed || saving || loading"
          :loading="saving"
          @click="saveDraft"
        >
          Сохранить
        </el-button>
        <el-button
          type="warning"
          data-action="close"
          :disabled="closed || saving || loading"
          @click="requestClose(false)"
        >
          Закрыть смену
        </el-button>
      </div>
    </div>

    <el-table :data="entries" row-key="taskId" border stripe style="width: 100%">
      <el-table-column prop="immName" label="ТПА" min-width="110" fixed />
      <el-table-column prop="productName" label="Наименование" min-width="170" />
      <el-table-column prop="remainingAtStart" label="Остаток на начало смены" min-width="130" align="right" />
      <el-table-column prop="plasticIngredients" label="Сырьё" min-width="160" />
      <el-table-column prop="colorantIngredients" label="Цвет/краситель" min-width="140" />

      <el-table-column label="Вес">
        <template #header><span data-group="weight">Вес</span></template>
        <el-table-column prop="normPartWeightGrams" label="Норма" min-width="90" align="right" />
        <el-table-column label="Изделие" min-width="140">
          <template #default="{ row }">
            <el-input-number
              :model-value="row.actualPartWeightGrams"
              :data-manual="`${row.taskId}:actualPartWeightGrams`"
              :disabled="closed || saving"
              :min="0"
              controls-position="right"
              @update:model-value="value => updateManual(row.taskId, 'actualPartWeightGrams', value)"
            />
            <div
              v-if="fieldErrors[`${row.taskId}:actualPartWeightGrams`]"
              :data-error="`${row.taskId}:actualPartWeightGrams`"
              class="field-error"
            >
              {{ fieldErrors[`${row.taskId}:actualPartWeightGrams`] }}
            </div>
          </template>
        </el-table-column>
      </el-table-column>

      <el-table-column label="Цикл">
        <template #header><span data-group="cycle">Цикл</span></template>
        <el-table-column prop="plannedFullCycleSeconds" label="Норма" min-width="90" align="right" />
        <el-table-column label="Автоматический" min-width="130" align="right">
          <template #default="{ row }">{{ formatDecimal(row.automaticAverageFullCycleSeconds) }}</template>
        </el-table-column>
        <el-table-column label="Ручной" min-width="140">
          <template #default="{ row }">
            <el-input-number
              :model-value="row.manualFullCycleSeconds"
              :data-manual="`${row.taskId}:manualFullCycleSeconds`"
              :disabled="closed || saving"
              :min="0"
              :precision="0"
              controls-position="right"
              @update:model-value="value => updateManual(row.taskId, 'manualFullCycleSeconds', value)"
            />
            <div
              v-if="fieldErrors[`${row.taskId}:manualFullCycleSeconds`]"
              :data-error="`${row.taskId}:manualFullCycleSeconds`"
              class="field-error"
            >
              {{ fieldErrors[`${row.taskId}:manualFullCycleSeconds`] }}
            </div>
          </template>
        </el-table-column>
      </el-table-column>

      <el-table-column label="Тарная единица" min-width="150">
        <template #default="{ row }">
          <div class="mb-1 text-xs text-gray-500">По умолчанию: {{ row.taskPackagingUnit ?? '—' }}</div>
          <el-input-number
            :model-value="row.actualPackagingUnit"
            :data-manual="`${row.taskId}:actualPackagingUnit`"
            :disabled="closed || saving"
            :min="0"
            :precision="0"
            controls-position="right"
            @update:model-value="value => updateManual(row.taskId, 'actualPackagingUnit', value)"
          />
          <div
            v-if="fieldErrors[`${row.taskId}:actualPackagingUnit`]"
            :data-error="`${row.taskId}:actualPackagingUnit`"
            class="field-error"
          >
            {{ fieldErrors[`${row.taskId}:actualPackagingUnit`] }}
          </div>
        </template>
      </el-table-column>
      <el-table-column label="Количество мест" min-width="150">
        <template #default="{ row }">
          <el-input-number
            :model-value="row.packagingPlaces"
            :data-manual="`${row.taskId}:packagingPlaces`"
            :disabled="closed || saving"
            :min="0"
            :precision="2"
            controls-position="right"
            @update:model-value="value => updateManual(row.taskId, 'packagingPlaces', value)"
          />
          <div
            v-if="fieldErrors[`${row.taskId}:packagingPlaces`]"
            :data-error="`${row.taskId}:packagingPlaces`"
            class="field-error"
          >
            {{ fieldErrors[`${row.taskId}:packagingPlaces`] }}
          </div>
        </template>
      </el-table-column>
      <el-table-column prop="cavities" label="Количество гнёзд" min-width="120" align="right" />
      <el-table-column prop="shiftPlanQuantity" label="План" min-width="90" align="right" />

      <el-table-column label="Выпуск">
        <template #header><span data-group="output">Выпуск</span></template>
        <el-table-column label="Автоматический" min-width="135" align="right">
          <template #default="{ row }">
            <span :data-computed="`${row.taskId}:automaticOutputQuantity`">{{ row.automaticOutputQuantity }}</span>
            <el-tag v-if="row.isManualFallbackRequired" :data-fallback="row.taskId" type="warning" size="small" class="ml-1">
              Ручной ввод
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="Факт отлитых (годные)" min-width="165">
          <template #default="{ row }">
            <el-input-number
              :model-value="row.manualGoodQuantity"
              :data-manual="`${row.taskId}:manualGoodQuantity`"
              :disabled="closed || saving"
              :min="0"
              :precision="0"
              controls-position="right"
              @update:model-value="value => updateManual(row.taskId, 'manualGoodQuantity', value)"
            />
            <div
              v-if="fieldErrors[`${row.taskId}:manualGoodQuantity`]"
              :data-error="`${row.taskId}:manualGoodQuantity`"
              class="field-error"
            >
              {{ fieldErrors[`${row.taskId}:manualGoodQuantity`] }}
            </div>
          </template>
        </el-table-column>
        <el-table-column label="Брак" min-width="120">
          <template #default="{ row }">
            <el-input-number
              :model-value="row.manualDefectQuantity"
              :data-manual="`${row.taskId}:manualDefectQuantity`"
              :disabled="closed || saving"
              :min="0"
              :precision="0"
              controls-position="right"
              @update:model-value="value => updateManual(row.taskId, 'manualDefectQuantity', value)"
            />
            <div
              v-if="fieldErrors[`${row.taskId}:manualDefectQuantity`]"
              :data-error="`${row.taskId}:manualDefectQuantity`"
              class="field-error"
            >
              {{ fieldErrors[`${row.taskId}:manualDefectQuantity`] }}
            </div>
          </template>
        </el-table-column>
      </el-table-column>

      <el-table-column label="Принято на склад" min-width="140">
        <template #default="{ row }"><span :data-cell="`${row.taskId}:warehouse`"></span></template>
      </el-table-column>
      <el-table-column label="Номер" min-width="90">
        <template #default="{ row }">
          <div v-for="operator in row.operatorTotals" :key="operator.operatorId">
            {{ operator.operatorEmployeeId || '—' }}
          </div>
        </template>
      </el-table-column>
      <el-table-column label="Литейщик" min-width="170">
        <template #default="{ row }">
          <div v-for="operator in row.operatorTotals" :key="operator.operatorId">
            {{ operator.operatorFullName }}
          </div>
        </template>
      </el-table-column>
      <el-table-column label="Автовыпуск литейщика" min-width="150" align="right">
        <template #default="{ row }">
          <div
            v-for="operator in row.operatorTotals"
            :key="operator.operatorId"
            :data-share="`${row.taskId}:${operator.operatorId}`"
          >
            {{ operator.quantity }}
          </div>
        </template>
      </el-table-column>
      <el-table-column label="Подпись" min-width="120">
        <template #default="{ row }"><span :data-cell="`${row.taskId}:signature`"></span></template>
      </el-table-column>
      <el-table-column label="Расход" min-width="110" align="right">
        <template #default="{ row }">{{ formatDecimal(row.plannedMaterialWeightGrams) }}</template>
      </el-table-column>
      <template #empty><span>Заданий в сменном рапорте нет</span></template>
    </el-table>

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
import { ElMessage } from 'element-plus'
import { useShiftClosureEditor } from '@/composables/useShiftClosureEditor'

const props = defineProps({
  itemId: { type: String, required: true }
})

const {
  report, loading, saving, fieldErrors, isDirty,
  load, updateManual, save, close
} = useShiftClosureEditor(props.itemId)

const earlyCloseDialog = ref(false)
const entries = computed(() => report.value?.entries ?? [])
const closed = computed(() => report.value?.state === 'Closed')

function formatDecimal(value) {
  if (value === null || value === undefined) return '—'
  return Number(value).toLocaleString('ru-RU', { maximumFractionDigits: 2 })
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

onMounted(() => load({ preserveDirty: true }))
defineExpose({ load })
</script>

<style scoped>
.shift-closure :deep(.el-input-number) {
  width: 125px;
}
.field-error {
  margin-top: 4px;
  color: #dc2626;
  font-size: 12px;
  line-height: 1.25;
  white-space: normal;
}
</style>
