<template>
  <el-dialog
    v-model="visible"
    :title="editingTask ? 'Редактирование задания' : 'Новое задание'"
    width="960px"
    :close-on-click-modal="false"
    @closed="onClosed"
  >
    <el-form 
      ref="formRef"
      :model="form" 
      :rules="rules" 
      label-width="140px"
      label-position="left"
    >
      <!-- Две колонки: поля попарно по смыслу, чтобы окно помещалось по высоте (FullHD) -->
      <div class="grid grid-cols-2 gap-x-6">
        <el-form-item label="ТПА" prop="immId">
          <el-select 
            v-model="form.immId" 
            placeholder="Выберите ТПА" 
            class="w-full"
            @focus="loadImms"
            :disabled="editingTask"
            :clearable="!editingTask"
          >
            <el-option
              v-for="imm in immOptions"
              :key="imm.id"
              :label="immLabel(imm)"
              :value="imm.id"
              :disabled="!imm.isActive"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Пресс-форма" prop="moldId">
          <el-select 
            v-model="form.moldId" 
            placeholder="Выберите пресс-форму" 
            class="w-full"
            @focus="loadMolds"
            :disabled="editingTask"
            :clearable="!editingTask"
            :no-data-text="moldNoDataText"
            filterable
          >
            <el-option
              v-for="mold in availableMolds"
              :key="mold.id"
              :label="`${mold.name} (${mold.cavities} гнёзд)`"
              :value="mold.id"
              :disabled="!mold.isActive"
            >
              <div class="flex justify-between items-center">
                <span>{{ mold.name }}</span>
                <el-tag size="small" type="info">{{ mold.cavities }} гнёзд</el-tag>
              </div>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="Изделие" prop="productId">
          <el-select
            v-model="form.productId"
            placeholder="Выберите изделие"
            class="w-full"
            filterable
            :disabled="isProductLocked || !form.moldId"
            @focus="loadProductTypes"
          >
            <el-option
              v-for="product in productOptions"
              :key="product.id"
              :label="`${product.fullArticle} — ${product.name}`"
              :value="product.id"
              :disabled="!product.isActive"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Заказ">
          <el-select
            v-model="form.orderId"
            placeholder="Выберите заказ"
            class="w-full"
            filterable
            :clearable="false"
            :no-data-text="orderNoDataText"
            :disabled="isRestricted || !!lockedOrder || !form.productId"
          >
            <el-option
              v-for="order in orderOptions"
              :key="order.id"
              :label="`${order.number}${order.productFullArticle ? ' (' + order.productFullArticle + ')' : ''}`"
              :value="order.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Наладчик" prop="personnelId">
          <el-select 
            v-model="form.personnelId" 
            placeholder="Не назначен"
            :clearable="!isLimitedEdit"
            class="w-full"
            @focus="loadPersonnel"
            :disabled="isBufferOnlyEdit"
            filterable
          >
            <el-option
              v-for="person in personnelOptions"
              :key="person.id"
              :label="person.fullName"
              :value="person.id"
              :disabled="person.notInList"
            >
              <div class="flex justify-between items-center">
                <span>{{ person.fullName }}</span>
                <el-tag size="small">{{ person.qualification || 'Наладчик' }}</el-tag>
              </div>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="План (шт.)" prop="planQuantity">
          <el-input-number 
            v-model="form.planQuantity" 
            data-test="plan-quantity"
            :min="1" 
            :max="1000000"
            :disabled="isRestricted"
            class="w-full"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Рабочий режим" prop="workMode">
          <el-select v-model="form.workMode" class="w-full" :disabled="isRestricted">
            <el-option label="Автомат" value="Auto" />
            <el-option label="Полуавтомат" value="SemiAuto" />
          </el-select>
        </el-form-item>
        <el-form-item label="Полный цикл, с" prop="plannedFullCycleSeconds" :required="isFullCycleRequired">
          <el-input-number
            v-model="form.plannedFullCycleSeconds"
            :min="1"
            :max="86400"
            :disabled="isRestricted"
            class="w-full"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Цикл литья, с" prop="plannedInjectionCycleSeconds">
          <el-input-number
            v-model="form.plannedInjectionCycleSeconds"
            :min="1"
            :max="86400"
            :disabled="isRestricted"
            class="w-full"
            controls-position="right"
          />
          <div class="text-xs text-gray-500 mt-1">Необязательно — нужен для анализа стабильности</div>
        </el-form-item>
        <el-form-item label="Плановая дата" prop="plannedDate">
          <el-date-picker
            v-model="form.plannedDate"
            type="date"
            placeholder="Выберите дату"
            format="DD.MM.YYYY"
            value-format="YYYY-MM-DD"
            :disabled-date="isPastDate"
            :disabled="isBufferOnlyEdit"
            class="w-full"
          />
        </el-form-item>
        <el-form-item label="Начало наладки" prop="plannedSetupStartTime">
          <el-time-picker
            v-model="form.plannedSetupStartTime"
            placeholder="Не задано"
            format="HH:mm"
            value-format="HH:mm:ss"
            clearable
            :disabled="isBufferOnlyEdit"
            class="w-full"
          />
          <div class="text-xs text-gray-500 mt-1">Необязательно. Пусто — задание начинается с начала смены</div>
        </el-form-item>
        <el-form-item label="Норматив наладки, мин" prop="plannedSetupMinutes">
          <el-input-number
            v-model="form.plannedSetupMinutes"
            data-test="setup-minutes" :value-on-clear="0"
            :min="0" :max="1440" :step="5"
            :disabled="isBufferOnlyEdit"
            class="w-full"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Буфер выпуска">
          <div class="flex items-center gap-2 w-full">
            <el-form-item prop="releaseBufferFixedMinutes" label-width="0" class="buffer-part">
              <el-input-number
                v-model="form.releaseBufferFixedMinutes"
                data-test="buffer-fixed-minutes" :value-on-clear="0"
                :min="0" :max="1440" :step="5"
                class="!w-24"
                controls-position="right"
              />
            </el-form-item>
            <span class="text-gray-500">мин +</span>
            <el-form-item prop="releaseBufferPercent" label-width="0" class="buffer-part">
              <el-input-number
                v-model="form.releaseBufferPercent"
                data-test="buffer-percent" :value-on-clear="0"
                :min="0" :max="100"
                class="!w-24"
                controls-position="right"
              />
            </el-form-item>
            <span class="text-gray-500">%</span>
          </div>
          <div class="text-xs text-gray-500 mt-1" data-test="plan-preview">{{ planPreview }}</div>
        </el-form-item>
        <el-form-item label="Примечание" class="col-span-2">
          <el-input
            v-model="form.note"
            type="textarea"
            :rows="2"
            :disabled="isRestricted"
            placeholder="Дополнительная информация"
          />
        </el-form-item>
      </div>

      <!-- Информация о пресс-форме (справочно) -->
      <el-alert
        v-if="selectedMoldInfo"
        title="Информация о пресс-форме"
        type="info"
        :closable="false"
        class="mt-4"
      >
        <div class="grid grid-cols-2 gap-2 text-sm">
          <div><span class="text-gray-500">Гнёздность:</span> {{ selectedMoldInfo.cavities }}</div>
          <div><span class="text-gray-500">Вес детали:</span> {{ selectedMoldInfo.partWeightGrams }} г</div>
          <div><span class="text-gray-500">Вес литника:</span> {{ selectedMoldInfo.runnerWeightGrams }} г</div>
          <div><span class="text-gray-500">Ресурс:</span> {{ selectedMoldInfo.remainingResource }} / {{ selectedMoldInfo.maxResourceCycles }}</div>
        </div>
      </el-alert>
    </el-form>

    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button type="primary" @click="handleSubmit" :loading="loading">
        Сохранить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, reactive, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { immApi } from '@/api/imm'
import { moldsApi } from '@/api/molds'
import { personnelApi } from '@/api/personnel'
import { tasksApi } from '@/api/tasks'
import { ordersApi } from '@/api/orders'
import { productTypesApi } from '@/api/productTypes'
import { activeProducts } from '@/utils/productLabel'
import { apiErrorMessage } from '@/utils/apiError'
import { settingsApi } from '@/api/settings'
import { isMoldCompatibleWithImm } from '@/utils/moldCompatibility'
import { calcTaskPlan, formatDuration } from '@/utils/taskPlan'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  task: {
    type: Object,
    default: null
  },
  // Заказ, из карточки которого открыли форму. Задан — заказ менять нельзя,
  // а изделие подставлено из заказа, список ПФ ограничен его типом изделия (см. спеку 2026-07-27, PZP-19).
  // Взаимоисключим с task: lockedOrder — только для создания нового задания,
  // при редактировании существующего (task задан) заказ через этот проп не подставляется.
  lockedOrder: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['update:modelValue', 'success'])

const visible = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value)
})

const formRef = ref(null)
const loading = ref(false)
const editingTask = ref(null)
const imms = ref([])
const molds = ref([])
const productTypes = ref([])
const personnel = ref([])
const orderOptions = ref([])
const initialOrderId = ref(null)

const form = reactive({
  immId: '',
  moldId: '',
  productId: null,
  personnelId: null,
  planQuantity: 1000,
  plannedDate: null,
  plannedSetupStartTime: null,
  note: '',
  orderId: null,
  workMode: 'Auto',
  plannedFullCycleSeconds: null,
  plannedInjectionCycleSeconds: null,
  plannedSetupMinutes: 0,
  releaseBufferFixedMinutes: 0,
  releaseBufferPercent: 0
})

const rules = {
  plannedDate: [{ required: true, message: 'Укажите плановую дату', trigger: 'change' }],
  immId: [{ required: true, message: 'Выберите ТПА', trigger: 'change' }],
  moldId: [{ required: true, message: 'Выберите пресс-форму', trigger: 'change' }],
  productId: [{ required: true, message: 'Выберите изделие', trigger: 'change' }],
  planQuantity: [
    { required: true, message: 'Введите план', trigger: 'blur' },
    { type: 'number', min: 1, message: 'План должен быть больше 0', trigger: 'blur' }
  ],
  plannedFullCycleSeconds: [
    {
      // При создании эталон обязателен. При редактировании — как на бэкенде
      // (UpdateTask_LegacyTaskWithoutFullCycle_SavesWithoutRequiringCycle):
      // legacy-задания без эталона (null) сохраняются и дальше без него;
      // обязателен он только если в задании уже есть непустое значение.
      validator: (rule, value, callback) => {
        const required = !editingTask.value || (editingTask.value.plannedFullCycleSeconds ?? null) !== null
        if (value === null || value === undefined) {
          return required ? callback(new Error('Введите эталон полного цикла')) : callback()
        }
        if (value < 1) return callback(new Error('Эталон должен быть больше 0'))
        callback()
      },
      trigger: 'blur'
    }
  ],
  plannedInjectionCycleSeconds: [
    {
      validator: (rule, value, callback) => {
        if (value === null || value === undefined) return callback()
        if (value < 1) return callback(new Error('Эталон должен быть больше 0'))
        if (form.plannedFullCycleSeconds && value > form.plannedFullCycleSeconds)
          return callback(new Error('Цикл литья не может превышать полный цикл'))
        callback()
      },
      trigger: 'blur'
    }
  ]
}

// То же условие, что и в validator'е rules.plannedFullCycleSeconds, вынесено
// сюда для визуальной звёздочки обязательности на el-form-item.
const isFullCycleRequired = computed(() => {
  return !editingTask.value || (editingTask.value.plannedFullCycleSeconds ?? null) !== null
})

// PZP-13/14: в «Выдано» правятся плановое начало, наладчик, норматив наладки и буфер
const isLimitedEdit = computed(() => editingTask.value?.status === 'Issued')
// PZP-14: после начала наладки и до завершения выпуска правится только буфер выпуска
const isBufferOnlyEdit = computed(() => ['Setup', 'InProgress'].includes(editingTask.value?.status))
// Поля, закрытые и в «Выдано», и в режиме «только буфер»
const isRestricted = computed(() => isLimitedEdit.value || isBufferOnlyEdit.value)

const selectedMoldInfo = computed(() => {
  if (!form.moldId) return null
  return molds.value.find(m => m.id === form.moldId)
})

// PZP-14: живой предпросмотр плана (гнёзда — из выбранной ПФ)
const planPreview = computed(() => {
  const plan = calcTaskPlan({
    planQuantity: form.planQuantity,
    cavities: selectedMoldInfo.value?.cavities,
    fullCycleSeconds: form.plannedFullCycleSeconds,
    setupMinutes: form.plannedSetupMinutes,
    bufferFixedMinutes: form.releaseBufferFixedMinutes,
    bufferPercent: form.releaseBufferPercent
  })
  return `Выпуск ≈ ${formatDuration(plan.releaseSeconds)} · буфер ≈ ${formatDuration(plan.bufferSeconds)}`
    + ` · всего с наладкой ≈ ${formatDuration(plan.totalSeconds)}`
})

// PZP-14: новое задание получает умолчания из настроек; менеджер может их поправить
const loadPlanningDefaults = async () => {
  try {
    const { data } = await settingsApi.getPlanningDefaults()
    form.plannedSetupMinutes = data.setupMinutes
    form.releaseBufferFixedMinutes = data.releaseBufferFixedMinutes
    form.releaseBufferPercent = data.releaseBufferPercent
  } catch (error) {
    // null → в запросе создания значения не заданы, сервер применит свои умолчания
    form.plannedSetupMinutes = null
    form.releaseBufferFixedMinutes = null
    form.releaseBufferPercent = null
    ElMessage.error('Ошибка загрузки умолчаний планирования')
  }
}

const selectedProductTypeId = computed(() => selectedMoldInfo.value?.productTypeId || null)

// PZP-19: изделия берём из типа выбранной ПФ
const productOptions = computed(() => {
  const type = productTypes.value.find(t => t.id === selectedProductTypeId.value)
  const list = activeProducts(type)
  // Текущее (архивное) изделие редактируемого задания показываем по артикулу из задания
  const task = editingTask.value
  if (task?.productId && !list.some(p => p.id === task.productId))
    return [...list, { id: task.productId, fullArticle: task.productFullArticle || task.productId, name: task.productName, isActive: false }]
  return list
})

// Изделие диктует заказ (из карточки заказа или уже привязанный); править его можно только в «Планируется»
const isProductLocked = computed(() =>
  !!props.lockedOrder || (!!editingTask.value && (editingTask.value.status !== 'Planned' || !!editingTask.value.orderId)))

const selectedProduct = computed(() => productOptions.value.find(p => p.id === form.productId) || null)

// PZP-16: при создании выбор ПФ и ТПА фильтруется по совместимости (пустой список ПФ — любой ТПА).
// При редактировании ТПА и ПФ заблокированы — фильтр не применяем, иначе ПФ «несовместимого»
// задания выпала бы из опций и селект показал бы сырой ID.
const filterByCompatibility = computed(() => !editingTask.value)

const availableMolds = computed(() => {
  let list = props.lockedOrder
    ? molds.value.filter(m => m.productTypeId === props.lockedOrder.productTypeId)
    : molds.value
  if (filterByCompatibility.value && form.immId) {
    list = list.filter(m => isMoldCompatibleWithImm(m, form.immId))
  }
  return list
})

const availableImms = computed(() => {
  if (!filterByCompatibility.value || !selectedMoldInfo.value) return imms.value
  return imms.value.filter(imm => isMoldCompatibleWithImm(selectedMoldInfo.value, imm.id))
})

// Текущее значение задания, которого нет в списке (архивный ТПА/наладчик или список ещё грузится),
// показываем по названию из задания — иначе el-select выводит голый id.
const immOptions = computed(() => {
  const task = editingTask.value
  if (!task?.immId || availableImms.value.some(i => i.id === task.immId)) return availableImms.value
  return [...availableImms.value, { id: task.immId, name: task.immName || task.immId, isActive: false }]
})

const personnelOptions = computed(() => {
  const task = editingTask.value
  if (!task?.personnelId || personnel.value.some(p => p.id === task.personnelId)) return personnel.value
  return [...personnel.value, { id: task.personnelId, fullName: task.personnelName || task.personnelId, notInList: true }]
})

const immLabel = (imm) => {
  const model = [imm.manufacturer, imm.model].filter(Boolean).join(' ')
  return model ? `${imm.name} (${model})` : imm.name
}

// Пустой список ПФ — не сбой: нет форм типа изделия заказа и/или совместимых с ТПА;
// без обоих условий остаётся стандартный текст Element Plus.
const moldNoDataText = computed(() => {
  const byImm = filterByCompatibility.value && !!form.immId
  if (props.lockedOrder) {
    if (byImm) return 'Нет пресс-форм этого типа изделия, совместимых с этим ТПА'
    const article = props.lockedOrder.productFullArticle
    return `Нет активных пресс-форм для изделия ${article || 'заказа'}`
  }
  return byImm ? 'Нет пресс-форм, совместимых с этим ТПА' : undefined
})

// Заказы подбираются по выбранному изделию (селект без изделия заблокирован).
const orderNoDataText = computed(() => {
  const article = selectedProduct.value?.fullArticle
  return `Нет активных заказов на изделие ${article || 'выбранное изделие'}`
})

const resetForm = () => {
  Object.assign(form, {
    immId: '',
    moldId: '',
    productId: props.lockedOrder?.productId ?? null,
    personnelId: null,
    planQuantity: 1000,
    plannedDate: null,
    plannedSetupStartTime: null,
    note: '',
    orderId: props.lockedOrder?.id ?? null,
    workMode: 'Auto',
    plannedFullCycleSeconds: null,
    plannedInjectionCycleSeconds: null,
    plannedSetupMinutes: 0,
    releaseBufferFixedMinutes: 0,
    releaseBufferPercent: 0
  })
  orderOptions.value = props.lockedOrder ? [props.lockedOrder] : []
  initialOrderId.value = null
}

const loadOrdersForProduct = async (productId) => {
  if (!productId) {
    orderOptions.value = []
    form.orderId = null
    return
  }
  try {
    const response = await ordersApi.getList({ status: 'Active', productId })
    orderOptions.value = response.data
    if (form.orderId && !orderOptions.value.some(o => o.id === form.orderId)) {
      form.orderId = null
    }
  } catch (error) {
    ElMessage.error('Ошибка загрузки заказов')
  }
}

watch(() => form.productId, (productId) => {
  // При заблокированном заказе список заказов не нужен, а вызов затёр бы form.orderId.
  if (props.lockedOrder) return
  loadOrdersForProduct(productId)
})

// Автоподстановка изделия по ПФ. Типы могут загрузиться позже ПФ — следим за обоими.
watch([selectedProductTypeId, productTypes], ([typeId]) => {
  if (props.lockedOrder) {
    form.productId = props.lockedOrder.productId
    return
  }
  // При редактировании изделие задания не трогаем
  if (editingTask.value) return
  if (typeId && productTypes.value.length === 0) {
    loadProductTypes()
    return
  }
  if (!productOptions.value.some(p => p.id === form.productId)) {
    form.productId = productOptions.value.length === 1 ? productOptions.value[0].id : null
  }
})

const loadMolds = async () => {
  if (molds.value.length > 0) return
  try {
    const response = await moldsApi.getList({ isActive: true })
    molds.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки пресс-форм')
  }
}

async function loadProductTypes() {
  if (productTypes.value.length > 0) return
  try {
    const response = await productTypesApi.getList({ isActive: true })
    productTypes.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки изделий')
  }
}

// Единственная опция селекта — сам заказ: отдельный запрос не нужен,
// карточка заказа уже передала всё для отображения.
const applyLockedOrder = () => {
  if (!props.lockedOrder) return
  // Режимы взаимоисключимы: task — это редактирование, где заказ меняется
  // только через ordersApi.setTaskOrder, а не как побочный эффект открытия формы.
  if (props.task) return
  orderOptions.value = [props.lockedOrder]
  form.orderId = props.lockedOrder.id
  form.productId = props.lockedOrder.productId
  // Список ПФ фильтруется по типу изделия заказа, поэтому нужен сразу,
  // не дожидаясь @focus на селекте.
  loadMolds()
  loadProductTypes()
}

watch(() => props.lockedOrder, applyLockedOrder, { immediate: true })

const isPastDate = (date) => {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return date < today
}

const populateForm = (task) => {
  editingTask.value = task
  Object.assign(form, {
    immId: task.immId,
    moldId: task.moldId,
    productId: task.productId ?? null,
    personnelId: task.personnelId ?? null,
    planQuantity: task.planQuantity,
    plannedDate: task.plannedDate,
    plannedSetupStartTime: task.plannedSetupStartTime ?? null,
    note: task.note || '',
    orderId: task.orderId || null,
    workMode: task.workMode || 'Auto',
    plannedFullCycleSeconds: task.plannedFullCycleSeconds ?? null,
    plannedInjectionCycleSeconds: task.plannedInjectionCycleSeconds ?? null,
    plannedSetupMinutes: task.plannedSetupMinutes ?? 0,
    releaseBufferFixedMinutes: task.releaseBufferFixedMinutes ?? 0,
    releaseBufferPercent: task.releaseBufferPercent ?? 0
  })
  initialOrderId.value = task.orderId || null
  // В режиме редактирования селект ПФ задизейблен и никогда не получает
  // @focus, поэтому molds (а значит и productTypeId для фильтра заказов)
  // нужно подгрузить явно, иначе селект «Заказ» останется пустым/disabled.
  loadMolds()
  loadProductTypes()
  // То же для ТПА (селект задизейблен) и наладчика: без списков селекты показывают id.
  loadImms()
  loadPersonnel()
}

watch(() => props.task, (newTask) => {
  if (newTask) {
    populateForm(newTask)
  } else {
    editingTask.value = null
    resetForm()
  }
}, { immediate: true })

watch(() => props.modelValue && !props.task, (creating) => {
  if (creating) loadPlanningDefaults()
}, { immediate: true })

// Перезаполняем при каждом открытии диалога, так как task может быть
// тем же объектом (та же ссылка) — watch выше не сработает повторно.
watch(() => props.modelValue, (isVisible) => {
  if (isVisible && props.task) {
    populateForm(props.task)
  }
  if (isVisible) {
    applyLockedOrder()
  }
})

// function, а не стрелка: populateForm вызывает её из watch(immediate) раньше этой строки
async function loadImms() {
  if (imms.value.length > 0) return
  try {
    const response = await immApi.getList({ isActive: true })
    imms.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}

async function loadPersonnel() {
  if (personnel.value.length > 0) return
  try {
    const response = await personnelApi.getList({ isActive: true, role: 'Adjuster' })
    personnel.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки персонала')
  }
}

const handleSubmit = async () => {
  if (!formRef.value) return

  await formRef.value.validate(async (valid) => {
    if (!valid) return

    loading.value = true
    try {
      if (editingTask.value) {
        // orderId и personnelId не входят в тело update — у них отдельные endpoints
        // (ordersApi.setTaskOrder, tasksApi.setPersonnel).
        const { orderId, personnelId, ...updatePayload } = form
        updatePayload.clearPlannedSetupStartTime = !form.plannedSetupStartTime
        // Наладчика меняем первым: его отказ не должен оставить частично сохранённое задание.
        if ((personnelId || null) !== (editingTask.value.personnelId || null)) {
          await tasksApi.setPersonnel(editingTask.value.id, personnelId || null)
        }
        await tasksApi.update(editingTask.value.id, updatePayload)
        if (form.orderId && form.orderId !== initialOrderId.value) {
          await ordersApi.setTaskOrder(editingTask.value.id, form.orderId)
        }
        ElMessage.success('Задание обновлено')
      } else {
        await tasksApi.create({ ...form, personnelId: form.personnelId || null })
        ElMessage.success('Задание создано в статусе «Планируется». Чтобы наладчик его увидел, нажмите «Выдать»')
      }

      visible.value = false
      emit('success')
    } catch (error) {
      ElMessage.error(apiErrorMessage(error, 'Ошибка сохранения задания'))
    } finally {
      loading.value = false
    }
  })
}

const onClosed = () => {
  editingTask.value = null
  resetForm()
}
</script>
<style scoped>
/* Части поля «Буфер выпуска» — вложенные el-form-item ради проверки каждого числа, без своего отступа снизу */
.buffer-part {
  margin-bottom: 0;
}
</style>
