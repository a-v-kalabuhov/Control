<template>
  <div v-loading="loading" class="shift-closure-cards">
    <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
      <div>
        <span v-if="closed" class="font-medium text-gray-700">Рапорт закрыт</span>
        <span v-else class="text-gray-600">Черновик сменного рапорта</span>
        <span v-if="isDirty" class="ml-2 text-sm text-amber-600">Есть несохранённые изменения</span>
      </div>
      <div v-if="!closed" class="flex gap-2">
        <el-button
          type="primary"
          data-action="save"
          :disabled="saving || loading"
          :loading="saving"
          @click="emit('save')"
        >
          Сохранить
        </el-button>
        <el-button
          type="warning"
          data-action="close"
          :disabled="saving || loading"
          @click="emit('close')"
        >
          Закрыть смену
        </el-button>
      </div>
    </div>

    <el-alert
      v-if="earlyCloseMessage"
      data-test="early-close-notice"
      class="mb-3"
      type="warning"
      :closable="false"
      :title="earlyCloseMessage"
    />

    <div v-if="entries.length" class="space-y-4">
      <section
        v-for="entry in entries"
        :key="entry.taskId"
        :data-closure-task="entry.taskId"
        class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm"
      >
        <div class="border-b border-gray-100 pb-3">
          <h3 class="text-lg font-semibold text-gray-800">{{ entry.immName }}</h3>
          <p class="mt-1 text-sm text-gray-600"><span class="font-medium">Наименование:</span> {{ entry.productName }}</p>
        </div>

        <dl class="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt class="text-gray-500">ТПА</dt>
            <dd class="font-medium text-gray-800">{{ entry.immName }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">Остаток на начало смены</dt>
            <dd class="font-medium text-gray-800">{{ entry.remainingAtStart }}</dd>
          </div>
          <div class="col-span-2">
            <dt class="text-gray-500">Сырьё</dt>
            <dd class="font-medium text-gray-800">{{ entry.plasticIngredients || '—' }}</dd>
          </div>
          <div class="col-span-2">
            <dt class="text-gray-500">Цвет/краситель</dt>
            <dd class="font-medium text-gray-800">{{ entry.colorantIngredients || '—' }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">Количество гнёзд</dt>
            <dd class="font-medium text-gray-800">{{ entry.cavities }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">План</dt>
            <dd class="font-medium text-gray-800">{{ entry.shiftPlanQuantity }}</dd>
          </div>
          <div>
            <dt class="text-gray-500">Расход, кг</dt>
            <dd :data-material-weight="entry.taskId" class="font-medium text-gray-800">
              {{ formatDecimal(entry.plannedMaterialWeightGrams / 1000) }}
            </dd>
          </div>
        </dl>

        <div class="mt-4 space-y-4">
          <section>
            <h4 class="mb-2 text-sm font-semibold text-gray-700">Вес</h4>
            <div class="grid grid-cols-2 gap-3">
              <div class="text-sm">
                <div class="text-gray-500">Норма</div>
                <div class="font-medium text-gray-800">{{ formatDecimal(entry.normPartWeightGrams) }}</div>
              </div>
              <label class="block text-sm">
                <span class="mb-1 block text-gray-600">Изделие</span>
                <el-input-number
                  :model-value="entry.actualPartWeightGrams"
                  :data-manual="`${entry.taskId}:actualPartWeightGrams`"
                  :disabled="closed || saving"
                  :min="0"
                  controls-position="right"
                  @update:model-value="value => emit('update-manual', entry.taskId, 'actualPartWeightGrams', value)"
                />
              </label>
            </div>
            <div
              v-if="fieldErrors[`${entry.taskId}:actualPartWeightGrams`]"
              :data-error="`${entry.taskId}:actualPartWeightGrams`"
              class="field-error"
            >
              {{ fieldErrors[`${entry.taskId}:actualPartWeightGrams`] }}
            </div>
          </section>

          <section>
            <h4 class="mb-2 text-sm font-semibold text-gray-700">Цикл</h4>
            <div class="grid grid-cols-2 gap-3 text-sm">
              <div>
                <div class="text-gray-500">Норма</div>
                <div class="font-medium text-gray-800">{{ entry.plannedFullCycleSeconds ?? '—' }}</div>
              </div>
              <div>
                <div class="text-gray-500">Автоматический</div>
                <div class="font-medium text-gray-800">{{ formatDecimal(entry.automaticAverageFullCycleSeconds) }}</div>
              </div>
            </div>
            <label class="mt-3 block text-sm">
              <span class="mb-1 block text-gray-600">Ручной</span>
              <el-input-number
                :model-value="entry.manualFullCycleSeconds"
                :data-manual="`${entry.taskId}:manualFullCycleSeconds`"
                :disabled="closed || saving"
                :min="0"
                :precision="0"
                controls-position="right"
                @update:model-value="value => emit('update-manual', entry.taskId, 'manualFullCycleSeconds', value)"
              />
            </label>
            <div
              v-if="fieldErrors[`${entry.taskId}:manualFullCycleSeconds`]"
              :data-error="`${entry.taskId}:manualFullCycleSeconds`"
              class="field-error"
            >
              {{ fieldErrors[`${entry.taskId}:manualFullCycleSeconds`] }}
            </div>
          </section>

          <section class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="block text-sm">
                <span class="mb-1 block font-semibold text-gray-700">Тарная единица</span>
                <span class="mb-1 block text-xs text-gray-500">По умолчанию: {{ entry.taskPackagingUnit ?? '—' }}</span>
                <el-input-number
                  :model-value="entry.actualPackagingUnit"
                  :data-manual="`${entry.taskId}:actualPackagingUnit`"
                  :disabled="closed || saving"
                  :min="0"
                  :precision="0"
                  controls-position="right"
                  @update:model-value="value => emit('update-manual', entry.taskId, 'actualPackagingUnit', value)"
                />
              </label>
              <div
                v-if="fieldErrors[`${entry.taskId}:actualPackagingUnit`]"
                :data-error="`${entry.taskId}:actualPackagingUnit`"
                class="field-error"
              >
                {{ fieldErrors[`${entry.taskId}:actualPackagingUnit`] }}
              </div>
            </div>
            <div>
              <label class="block text-sm">
                <span class="mb-1 block font-semibold text-gray-700">Количество мест</span>
                <el-input-number
                  :model-value="entry.packagingPlaces"
                  :data-manual="`${entry.taskId}:packagingPlaces`"
                  :disabled="closed || saving"
                  :min="0"
                  :precision="2"
                  controls-position="right"
                  @update:model-value="value => emit('update-manual', entry.taskId, 'packagingPlaces', value)"
                />
              </label>
              <div
                v-if="fieldErrors[`${entry.taskId}:packagingPlaces`]"
                :data-error="`${entry.taskId}:packagingPlaces`"
                class="field-error"
              >
                {{ fieldErrors[`${entry.taskId}:packagingPlaces`] }}
              </div>
            </div>
          </section>

          <section>
            <h4 class="mb-2 text-sm font-semibold text-gray-700">Выпуск</h4>
            <div class="mb-3 text-sm">
              <span class="text-gray-500">Автоматический: </span>
              <span :data-computed="`${entry.taskId}:automaticOutputQuantity`" class="font-medium text-gray-800">
                {{ entry.automaticOutputQuantity }}
              </span>
              <el-tag
                v-if="entry.isManualFallbackRequired"
                :data-fallback="entry.taskId"
                type="warning"
                size="small"
                class="ml-2"
              >
                Ручной ввод
              </el-tag>
            </div>
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label class="block text-sm">
                  <span class="mb-1 block text-gray-600">Факт отлитых (годные)</span>
                  <el-input-number
                    :model-value="entry.manualGoodQuantity"
                    :data-manual="`${entry.taskId}:manualGoodQuantity`"
                    :disabled="closed || saving"
                    :min="0"
                    :precision="0"
                    controls-position="right"
                    @update:model-value="value => emit('update-manual', entry.taskId, 'manualGoodQuantity', value)"
                  />
                </label>
                <div
                  v-if="fieldErrors[`${entry.taskId}:manualGoodQuantity`]"
                  :data-error="`${entry.taskId}:manualGoodQuantity`"
                  class="field-error"
                >
                  {{ fieldErrors[`${entry.taskId}:manualGoodQuantity`] }}
                </div>
              </div>
              <div>
                <label class="block text-sm">
                  <span class="mb-1 block text-gray-600">Брак</span>
                  <el-input-number
                    :model-value="entry.manualDefectQuantity"
                    :data-manual="`${entry.taskId}:manualDefectQuantity`"
                    :disabled="closed || saving"
                    :min="0"
                    :precision="0"
                    controls-position="right"
                    @update:model-value="value => emit('update-manual', entry.taskId, 'manualDefectQuantity', value)"
                  />
                </label>
                <div
                  v-if="fieldErrors[`${entry.taskId}:manualDefectQuantity`]"
                  :data-error="`${entry.taskId}:manualDefectQuantity`"
                  class="field-error"
                >
                  {{ fieldErrors[`${entry.taskId}:manualDefectQuantity`] }}
                </div>
              </div>
            </div>
          </section>
        </div>

        <div class="mt-4 grid grid-cols-2 gap-4 border-t border-gray-100 pt-3 text-sm">
          <div>
            <div class="text-gray-500">Принято на склад</div>
            <span :data-cell="`${entry.taskId}:warehouse`"></span>
          </div>
          <div>
            <div class="text-gray-500">Подпись</div>
            <span :data-cell="`${entry.taskId}:signature`"></span>
          </div>
        </div>

        <div v-if="entry.operatorTotals?.length" class="mt-4 border-t border-gray-100 pt-3">
          <div class="mb-2 grid grid-cols-[64px_1fr_80px] gap-2 text-xs text-gray-500">
            <span>Номер</span>
            <span>Литейщик</span>
            <span class="text-right">Автовыпуск литейщика</span>
          </div>
          <div
            v-for="operator in entry.operatorTotals"
            :key="operator.operatorId"
            class="grid grid-cols-[64px_1fr_80px] gap-2 py-1 text-sm"
          >
            <span>{{ operator.operatorEmployeeId || '—' }}</span>
            <span>{{ operator.operatorFullName }}</span>
            <span :data-share="`${entry.taskId}:${operator.operatorId}`" class="text-right font-medium">
              {{ operator.quantity }}
            </span>
          </div>
        </div>
      </section>
    </div>
    <el-empty v-else-if="!loading" description="Заданий в сменном рапорте нет" />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import dayjs from 'dayjs'

defineOptions({ name: 'ShiftClosureCards' })

const props = defineProps({
  report: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  saving: { type: Boolean, default: false },
  fieldErrors: { type: Object, default: () => ({}) },
  isDirty: { type: Boolean, default: false }
})

const emit = defineEmits(['update-manual', 'save', 'close'])
const entries = computed(() => props.report?.entries ?? [])
const closed = computed(() => props.report?.state === 'Closed')
const earlyCloseMessage = computed(() => {
  if (!props.report?.closedEarly || !props.report.closedAt) return null
  return `Смена закрыта досрочно: фактическое время закрытия ${dayjs(props.report.closedAt).format('DD.MM.YYYY HH:mm')}, ` +
    `плановое окончание ${dayjs(props.report.endAtUtc).format('DD.MM.YYYY HH:mm')}`
})

function formatDecimal(value) {
  if (value === null || value === undefined) return '—'
  return Number(value).toLocaleString('ru-RU', { maximumFractionDigits: 2 })
}
</script>

<style scoped>
.shift-closure-cards :deep(.el-input-number) {
  width: 100%;
}
.field-error {
  margin-top: 4px;
  color: #dc2626;
  font-size: 12px;
  line-height: 1.25;
  white-space: normal;
}
</style>
