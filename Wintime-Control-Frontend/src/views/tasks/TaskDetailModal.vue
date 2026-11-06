<template>
  <el-dialog
    v-model="visible"
    title="Детали задания"
    width="800px"
    :close-on-click-modal="false"
    :append-to-body="appendToBody"
  >
    <div v-loading="loading">
      <el-tabs v-model="activeTab">
        <el-tab-pane label="Данные задания" name="data">
          <!-- Основная информация -->
          <el-descriptions :column="2" border class="mb-6">
            <el-descriptions-item label="Статус">
              <TaskStatusBadge :status="task?.status" />
            </el-descriptions-item>
            <el-descriptions-item label="Плановое начало">
              {{ task ? formatPlannedStart(task) : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Выдано">
              {{ formatDate(task?.issuedAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="Оборудование">
              {{ task?.immName }}
            </el-descriptions-item>
            <el-descriptions-item label="Пресс-форма">
              {{ task?.moldName }}
            </el-descriptions-item>
            <el-descriptions-item label="Изделие">
              <div class="font-semibold">{{ task?.productFullArticle }}</div>
              <div v-if="productSecondary(task?.productTypeName, task?.productName)" class="text-sm text-gray-500">
                {{ productSecondary(task?.productTypeName, task?.productName) }}
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="Наладчик">
              {{ task?.personnelName || '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Заказ">
              {{ orderLabel }}
            </el-descriptions-item>
            <el-descriptions-item label="Заказчик">
              <div class="flex items-center gap-1">
                <span>{{ customerLabel }}</span>
                <el-tooltip v-if="customerMismatch" :content="customerMismatchHint">
                  <el-icon class="text-amber-500" data-test="customer-mismatch"><Warning /></el-icon>
                </el-tooltip>
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="Рабочий режим">
              {{ workModeLabel }}
            </el-descriptions-item>
            <el-descriptions-item label="Вес детали">
              {{ task?.partWeightGrams !== null && task?.partWeightGrams !== undefined ? `${task.partWeightGrams} г` : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Тарная единица">
              {{ task?.packagingUnit ? `${task.packagingUnit} шт.` : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Полный цикл">
              {{ task?.plannedFullCycleSeconds ? `${task.plannedFullCycleSeconds} с` : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Цикл литья">
              {{ task?.plannedInjectionCycleSeconds ? `${task.plannedInjectionCycleSeconds} с` : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Норматив наладки">
              {{ task ? formatDuration((task.plannedSetupMinutes ?? 0) * 60) : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="Выпуск (расчёт)">
              {{ formatDuration(task?.plannedReleaseSeconds) }}
            </el-descriptions-item>
            <el-descriptions-item label="Буфер выпуска">
              {{ bufferLabel }}
            </el-descriptions-item>
            <el-descriptions-item label="Плановое окончание">
              {{ formatDate(task?.plannedEndAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="Начато">
              {{ formatDate(task?.startedAt) }}
            </el-descriptions-item>
            <el-descriptions-item v-if="task?.completedAt" label="Завершено">
              {{ formatDate(task?.completedAt) }}
            </el-descriptions-item>
            <el-descriptions-item v-if="task?.closedAt" label="Закрыто">
              {{ formatDate(task?.closedAt) }}
            </el-descriptions-item>
            <el-descriptions-item
              v-if="canClose || (task?.closeReason && task?.status === 'Closed')"
              :label="canClose ? 'Причина закрытия досрочно' : 'Причина закрытия'"
              :span="2"
            >
              <el-input
                v-if="canClose"
                v-model="closeReasonInput"
                type="textarea"
                :rows="2"
                placeholder="Укажите причину закрытия задания"
              />
              <span v-else>{{ task.closeReason }}</span>
            </el-descriptions-item>
            <el-descriptions-item
              v-if="task?.closeReason && task?.status === 'Completed'"
              label="Причина отклонения от плана"
              :span="2"
            >
              <span>{{ task.closeReason }}</span>
            </el-descriptions-item>
          </el-descriptions>

          <!-- Рецепт задания (BL-36): копия рецепта изделия + замена материала менеджером -->
          <el-card class="mb-6">
            <template #header>
              <div class="flex items-center justify-between">
                <span class="font-semibold">Рецепт</span>
                <el-tag v-if="task?.recipeEdited" size="small" type="warning">изменён вручную</el-tag>
              </div>
            </template>
            <div v-if="!task?.recipeIngredients?.length" class="text-gray-500">Рецепт не задан</div>
            <template v-else>
              <el-table :data="task.recipeIngredients" size="small">
                <el-table-column label="Тип" width="120">
                  <template #default="{ row }">{{ materialTypeLabel(row.materialType) }}</template>
                </el-table-column>
                <el-table-column label="Материал">
                  <template #default="{ row }">
                    <div>{{ row.materialArticle }} — {{ row.materialName }}</div>
                    <div v-if="row.materialId !== row.originalMaterialId" class="text-sm text-gray-500">
                      было: {{ row.originalMaterialArticle }} — {{ row.originalMaterialName }}
                    </div>
                  </template>
                </el-table-column>
                <el-table-column label="%" width="90" align="right">
                  <template #default="{ row }">{{ formatPercent(row.percent) }}</template>
                </el-table-column>
                <el-table-column v-if="canReplaceIngredient" width="120">
                  <template #default="{ row }">
                    <el-button class="recipe-replace" size="small" @click="openReplace(row)">Заменить</el-button>
                  </template>
                </el-table-column>
              </el-table>
              <div class="recipe-sum mt-2 text-sm" :class="recipeSumClass">{{ recipeSumText }}</div>
            </template>
          </el-card>

          <ReplaceIngredientDialog
            v-model="replaceVisible"
            :task="task"
            :ingredient="replaceRow"
            @replaced="onReplaced"
          />

          <!-- Прогресс -->
          <el-card class="mb-6">
            <template #header>
              <span class="font-semibold">Выполнение</span>
            </template>
            <TaskProgress
              :plan-quantity="task?.planQuantity"
              :actual-quantity="task?.actualQuantity"
            />
          </el-card>

          <!-- Конфликты плана (PZP-07b): подсветка, ничего не блокирует -->
          <el-card v-if="taskConflicts.length" class="mb-6" data-test="task-conflicts">
            <template #header>
              <span class="font-semibold">Конфликты</span>
            </template>
            <ul class="space-y-1">
              <li
                v-for="(c, i) in taskConflicts"
                :key="i"
                data-test="task-conflict-row"
                :class="readonly ? '' : 'cursor-pointer hover:underline'"
                :style="{ color: CONFLICT_KIND_COLORS[c.kind] }"
                @click="!readonly && $emit('open-task', otherSide(c, task.id).id)"
              >
                {{ conflictLine(c, task.id) }}
              </li>
            </ul>
          </el-card>

          <!-- Примечания -->
          <el-card v-if="task?.note">
            <template #header>
              <span class="font-semibold">Примечания</span>
            </template>
            <p class="text-gray-800">{{ task.note }}</p>
          </el-card>
        </el-tab-pane>
        <el-tab-pane label="Результаты выполнения" name="results">
          <TaskResultsTab v-if="visible && activeTab === 'results' && task?.id" :task-id="task.id" />
        </el-tab-pane>
      </el-tabs>

      <!-- Действия -->
      <div class="flex justify-between mt-6">
        <el-button @click="visible = false">Назад</el-button>
        <div class="flex gap-4">
          <el-button
            v-if="canEdit"
            @click="$emit('edit', task)"
          >
            Редактировать
          </el-button>
          <el-button v-if="canIssue" type="warning" @click="$emit('issue', task)">Выдать</el-button>
          <el-button
            v-if="canComplete"
            type="success"
            @click="$emit('complete', task)"
          >
            Завершить
          </el-button>
          <el-button
            v-if="canClose"
            type="danger"
            :disabled="!closeReasonInput.trim()"
            @click="$emit('close', task, closeReasonInput.trim())"
          >
            Закрыть задание
          </el-button>
        </div>
      </div>
    </div>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import TaskProgress from '@/components/tasks/TaskProgress.vue'
import TaskResultsTab from '@/components/tasks/TaskResultsTab.vue'
import { formatPlannedStart } from '@/utils/plannedStart'
import dayjs from 'dayjs'
import { Warning } from '@element-plus/icons-vue'
import { formatDuration } from '@/utils/taskPlan'
import { usePlanConflictsStore } from '@/stores/planConflicts'
import { conflictLine, otherSide, CONFLICT_KIND_COLORS } from '@/utils/planConflicts'
import { productSecondary } from '@/utils/productLabel'
import ReplaceIngredientDialog from './ReplaceIngredientDialog.vue'
import { materialTypeLabel, plasticSum, formatPercent, isRecipeSumOk, recipeSumHint } from '@/utils/recipe'
import { taskCustomer, hasTaskCustomerMismatch } from '@/utils/taskCustomer'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  task: {
    type: Object,
    default: null
  },
  // Только просмотр (из хронологии карточки ТПА): без кнопок действий и поля причины закрытия
  readonly: {
    type: Boolean,
    default: false
  },
  // Открыта поверх другого окна (карточки ТПА) — вложенный el-dialog должен рендериться в body
  appendToBody: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:modelValue', 'edit', 'issue', 'complete', 'close', 'open-task', 'recipe-changed'])

const planConflicts = usePlanConflictsStore()
const taskConflicts = computed(() => (props.task?.id ? planConflicts.forTask(props.task.id) : []))

const authStore = useAuthStore()
const loading = ref(false)
const closeReasonInput = ref('')
const activeTab = ref('data')

// BL-36: замена ингредиента копии рецепта — диалог, обновление приходит ответом TaskDto.
const replaceVisible = ref(false)
const replaceRow = ref(null)

const canReplaceIngredient = computed(() =>
  !props.readonly && !!props.task && (authStore.isManager || authStore.isAdmin))

const recipeSumText = computed(() => {
  const rows = props.task?.recipeIngredients ?? []
  const base = `Пластики: ${formatPercent(plasticSum(rows))} %`
  return isRecipeSumOk(rows) ? base : `${base} — ${recipeSumHint(rows)}`
})

const recipeSumClass = computed(() =>
  isRecipeSumOk(props.task?.recipeIngredients ?? []) ? 'text-gray-500' : 'text-red-600 font-medium')

function openReplace(row) {
  replaceRow.value = row
  replaceVisible.value = true
}

function onReplaced(dto) {
  emit('recipe-changed', dto)
}

const visible = computed({
  get: () => props.modelValue,
  set: (value) => {
    if (!value) {
      closeReasonInput.value = ''
    }
    emit('update:modelValue', value)
  }
})

// Закрытие снаружи (modelValue=false от родителя) тоже возвращает на «Данные задания»
watch(() => props.modelValue, v => { if (!v) activeTab.value = 'data' })

// Другое задание — снова с «Данных задания» (итоги прошлого задания не показываем)
watch(() => props.task?.id, () => { activeTab.value = 'data' })

const canEdit = computed(() => {
  if (!props.task || props.readonly) return false
  // PZP-14: до завершения — ограниченная форма; после — только вес детали (PZP-24).
  return authStore.isManager || authStore.isAdmin
})

const canIssue = computed(() => {
  if (!props.task || props.readonly) return false
  return props.task.status === 'Planned' && authStore.isManager
})

const canComplete = computed(() => {
  if (!props.task || props.readonly) return false
  return props.task.status === 'InProgress' && authStore.isAdjuster
})

const canClose = computed(() => {
  if (!props.task || props.readonly) return false
  return ['Completed', 'InProgress'].includes(props.task.status) && authStore.isManager
})

const workModeLabel = computed(() => {
  if (!props.task?.workMode) return '—'
  return props.task.workMode === 'SemiAuto' ? 'Полуавтомат' : 'Автомат'
})

// PZP-14: буфер с расшифровкой формулы «фикс. мин + % от выпуска»
const bufferLabel = computed(() => {
  const seconds = props.task?.plannedReleaseBufferSeconds
  if (seconds === null || seconds === undefined) return '—'
  return `${formatDuration(seconds)} (${props.task.releaseBufferFixedMinutes} мин + ${props.task.releaseBufferPercent} %)`
})

// «ORD-77 от 01.10.2026»; дата у заказа необязательна
const orderLabel = computed(() => {
  if (!props.task?.orderNumber) return '—'
  const date = props.task.orderDate ? ` от ${dayjs(props.task.orderDate).format('DD.MM.YYYY')}` : ''
  return `${props.task.orderNumber}${date}`
})

// PZP-23: привязка заказа важнее наличия текста. Если заказ привязан без заказчика,
// компанию типа не подставляем — это два независимых справочных значения.
const customerLabel = computed(() => taskCustomer(props.task) || '—')
const customerMismatch = computed(() => hasTaskCustomerMismatch(props.task))

const customerMismatchHint = computed(() =>
  `В заказе: ${props.task?.orderCustomer}; в типе изделия: ${props.task?.productTypeCompany}`)

const formatDate = (date) => {
  if (!date) return '—'
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}
</script>