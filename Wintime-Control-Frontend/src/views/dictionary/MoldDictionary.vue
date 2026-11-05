<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник пресс-форм</h2>
        <p class="text-gray-600 mt-1">Управление оснасткой и учёт ресурса</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить пресс-форму
      </el-button>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Наименование или артикул" clearable />
        </el-form-item>
        <el-form-item label="Заказчик">
          <CustomerFilterSelect
            v-model:customer="filters.customer"
            v-model:customer-empty="filters.customerEmpty"
            :fetch="fetchCompanies"
            style="width: 260px;"
          />
        </el-form-item>
        <el-form-item label="Статус">
          <el-select v-model="filters.isActive" placeholder="Все" clearable style="width: 160px;">
            <el-option label="Активные" :value="true" />
            <el-option label="Не активные" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadMolds">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Таблица пресс-форм -->
    <el-table :data="molds" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="formId" label="Артикул" width="120" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column label="Тип изделия" width="200">
        <template #default="{ row }">
          <span v-if="row.productTypeName">{{ row.productTypeArticle }} · {{ row.productTypeName }}</span>
          <el-tag v-else type="warning" size="small">не задан</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="ТПА" width="180" show-overflow-tooltip>
        <template #default="{ row }">
          <span v-if="row.compatibleImms?.length">{{ formatCompatibleImms(row.compatibleImms) }}</span>
          <span v-else class="text-gray-400">любой</span>
        </template>
      </el-table-column>
      <el-table-column prop="cavities" label="Гнёзд" width="80" align="center" />
      <el-table-column prop="runnerWeightGrams" label="Литник (г)" width="120" />
      <el-table-column prop="storageLocationIndex" label="Место хранения" width="120" />
      <el-table-column label="Особенности" width="140" align="center">
        <template #default="{ row }">
          <div class="flex items-center justify-center gap-2">
            <el-tooltip v-if="row.hasMovingParts" content="Подвижные знаки" placement="top">
              <el-icon data-test="mold-feature-moving-parts" :size="18" aria-label="Подвижные знаки"><Rank /></el-icon>
            </el-tooltip>
            <el-tooltip v-if="row.requiresRobot" content="Нужен робот" placement="top">
              <el-icon data-test="mold-feature-robot" :size="18" aria-label="Нужен робот"><Cpu /></el-icon>
            </el-tooltip>
            <el-tooltip v-if="row.requiresConveyor" content="Нужен конвейер" placement="top">
              <el-icon data-test="mold-feature-conveyor" :size="18" aria-label="Нужен конвейер"><Van /></el-icon>
            </el-tooltip>
            <span
              v-if="!row.hasMovingParts && !row.requiresRobot && !row.requiresConveyor"
              data-test="mold-features-empty"
              class="text-gray-400"
            >—</span>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="Остаток ресурса" width="150">
        <template #default="{ row }">
          <el-progress
            :percentage="Math.round((row.remainingResource / row.maxResourceCycles) * 100)"
            :status="(row.maxResourceCycles - row.remainingResource) > row.to2Cycles ? 'exception' : (row.maxResourceCycles - row.remainingResource) > row.to1Cycles ? 'warning' : ''"
            :format="p => p + '%'"
          />
          <div class="text-xs text-gray-500">{{ row.remainingResource }} / {{ row.maxResourceCycles }}</div>
        </template>
      </el-table-column>
      <el-table-column label="Статус" width="100" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активна' : 'Не активна' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="150" fixed="right">
        <template #default="{ row }">
          <div class="flex flex-col gap-1 items-start">
            <el-button size="small" style="width: 130px; margin-left: 0" @click="editMold(row)">Редактировать</el-button>
            <el-button size="small" style="width: 130px; margin-left: 0" @click="showQr(row)">QR</el-button>
            <el-button size="small" style="width: 130px; margin-left: 0" type="danger" @click="deleteMold(row)">Удалить</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <!-- Модальное окно -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingMold ? 'Редактирование пресс-формы' : 'Новая пресс-форма'"
      width="700px"
    >
      <el-form :model="form" label-width="180px" :rules="rules" ref="formRef">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Артикул" prop="formId" required>
              <el-input v-model="form.formId" placeholder="PF-001" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Наименование" prop="name" required>
              <el-input v-model="form.name" placeholder="КлипДак (48)" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="24">
            <el-form-item label="Тип изделия" prop="productTypeId" required>
              <el-select
                v-model="form.productTypeId"
                filterable
                placeholder="Выберите изделие"
                class="w-full"
              >
                <el-option
                  v-for="pt in selectableProductTypes"
                  :key="pt.id"
                  :label="`${pt.article} · ${pt.name}`"
                  :value="pt.id"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item v-if="editingMold" label="Заказчик">
          <span class="text-gray-800">{{ selectedProductTypeCompany || '—' }}</span>
        </el-form-item>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Количество гнёзд" prop="cavities" required>
              <el-input-number v-model="form.cavities" :min="1" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Вес литника (г)">
              <el-input-number v-model="form.runnerWeightGrams" :precision="2" :step="0.1" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item v-if="editingMold && productWeightRows.length" label="Вес детали">
          <div class="text-sm">
            <div v-for="row in productWeightRows" :key="row.productId ?? 'all'">
              <span v-if="row.fullArticle">{{ row.fullArticle }}: </span>
              {{ row.partWeightGrams }} г · за смыкание {{ row.shotWeightGrams ?? '—' }} г
            </div>
            <div class="text-xs text-gray-500">Вес задаётся в изделиях; вес за смыкание = деталь × гнёзда</div>
          </div>
        </el-form-item>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Ресурс (смыкания)" required>
              <el-input-number v-model="form.maxResourceCycles" :min="1" class="w-full" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Место хранения">
              <el-input v-model="form.storageLocationIndex" placeholder="А-12" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row v-if="editingMold" :gutter="20">
          <el-col :span="12">
            <el-form-item label="Выполнено смыканий">
              <span class="text-gray-800 font-medium">
                {{ (editingMold.maxResourceCycles - editingMold.remainingResource).toLocaleString('ru-RU') }}
              </span>
              <span class="text-gray-400 ml-2">
                из {{ editingMold.maxResourceCycles.toLocaleString('ru-RU') }}
              </span>
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="ТО-1 (смыкания)">
              <el-input-number v-model="form.to1Cycles" :min="0" class="w-full" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="ТО-2 (смыкания)">
              <el-input-number v-model="form.to2Cycles" :min="0" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item label="Особенности">
          <div class="flex flex-col items-start gap-1">
            <el-checkbox v-model="form.hasMovingParts" :disabled="featuresDisabled">Подвижные знаки</el-checkbox>
            <el-checkbox v-model="form.requiresRobot" :disabled="featuresDisabled">Нужен робот</el-checkbox>
            <el-checkbox v-model="form.requiresConveyor" :disabled="featuresDisabled">Нужен конвейер</el-checkbox>
            <div v-if="featuresDisabled" class="text-xs text-gray-500">
              У архивной пресс-формы особенности доступны только для чтения
            </div>
          </div>
        </el-form-item>

        <el-form-item label="Совместимые ТПА">
          <el-select
            v-model="form.compatibleImmIds"
            multiple
            filterable
            clearable
            placeholder="Любой ТПА"
            class="w-full"
          >
            <el-option
              v-for="imm in selectableImms"
              :key="imm.id"
              :label="immOptionLabel(imm)"
              :value="imm.id"
            />
          </el-select>
          <div class="text-xs text-gray-500">Пусто — подходит к любому ТПА</div>
        </el-form-item>

        <el-form-item label="Статус">
          <el-switch v-model="form.isActive" active-text="Активна" inactive-text="Не активна" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveMold" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>

    <QrCodeDialog v-model="qrDialogVisible" :qr-data="qrData" :label="qrLabel" />
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Cpu, Rank, Van } from '@element-plus/icons-vue'
import { moldsApi } from '@/api/molds'
import { productTypesApi } from '@/api/productTypes'
import { immApi } from '@/api/imm'
import { immOptionLabel, formatCompatibleImms } from '@/utils/moldCompatibility'
import QrCodeDialog from '@/components/common/QrCodeDialog.vue'
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'
import { apiErrorMessage } from '@/utils/apiError'
import dayjs from 'dayjs'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingMold = ref(null)
const formRef = ref(null)
const featuresDisabled = computed(() => editingMold.value?.isActive === false)

const molds = ref([])
const productTypes = ref([])
const imms = ref([])

// PZP-16: опции — активные ТПА плюс уже привязанные к редактируемой ПФ архивные.
// Архивные не disabled: тег отключённой опции в Element Plus снять нельзя, а повторно
// добавить ранее привязанный архивный ТПА сервер разрешает.
const selectableImms = computed(() => {
  const linkedArchived = (editingMold.value?.compatibleImms ?? [])
    .filter(i => !i.isActive && !imms.value.some(a => a.id === i.id))
  return [...imms.value, ...linkedArchived]
})
// Синтетическая опция для архивного типа изделия текущей редактируемой пресс-формы
// (не входит в productTypes, т.к. там только активные типы) — не влияет на список создания.
const editingArchivedType = ref(null)
const selectableProductTypes = computed(() => {
  const base = productTypes.value
  const extra = editingArchivedType.value
  if (extra && !base.some(pt => pt.id === extra.id)) return [...base, extra]
  return base
})

const selectedProductTypeCompany = computed(() => {
  const selected = selectableProductTypes.value.find(t => t.id === form.productTypeId)
  if (selected) return selected.company ?? null
  return form.productTypeId === editingMold.value?.productTypeId
    ? editingMold.value?.productTypeCompany
    : null
})

// PZP-24: вес детали принадлежит изделию. Если у всего типа он одинаков,
// карточка ПФ показывает одну строку; разные веса — по строке на изделие.
const productWeightRows = computed(() => {
  const type = productTypes.value.find(t => t.id === form.productTypeId)
  const source = type?.products?.length
    ? type.products.map(p => ({
        productId: p.id, fullArticle: p.fullArticle,
        partWeightGrams: p.partWeightGrams ?? 0
      }))
    : form.productTypeId === editingMold.value?.productTypeId
      ? (editingMold.value?.productWeights ?? [])
      : []
  const rows = source.map(p => ({
    ...p,
    shotWeightGrams: (p.partWeightGrams ?? 0) * (form.cavities || 0)
  }))
  if (rows.length > 1 && rows.every(r => r.partWeightGrams === rows[0].partWeightGrams)) {
    return [{ ...rows[0], fullArticle: '' }]
  }
  return rows
})

const qrDialogVisible = ref(false)
const qrData = ref('')
const qrLabel = ref('')

const showQr = async (mold) => {
  try {
    const response = await moldsApi.getQr(mold.id)
    qrData.value = response.data.qrData
    qrLabel.value = `${mold.formId} · ${mold.name}`
    qrDialogVisible.value = true
  } catch (error) {
    ElMessage.error('Ошибка получения QR-кода')
  }
}

const filters = reactive({
  search: '',
  isActive: null,
  customer: null,
  customerEmpty: false
})

async function fetchCompanies(search) {
  const { data } = await productTypesApi.suggestCompanies(search)
  return data
}

const form = reactive({
  formId: '',
  name: '',
  cavities: 1,
  runnerWeightGrams: 0,
  maxResourceCycles: 0,
  to1Cycles: 0,
  to2Cycles: 0,
  storageLocationIndex: '',
  isActive: true,
  productTypeId: null,
  hasMovingParts: false,
  requiresRobot: false,
  requiresConveyor: false,
  compatibleImmIds: [],
})

const validateFormId = async (rule, value, callback) => {
  if (!value?.trim()) {
    callback(new Error('Введите артикул'))
    return
  }
  const response = await moldsApi.getList({ search: value.trim() })
  const duplicate = response.data.find(
    m => m.formId === value.trim() && m.id !== editingMold.value?.id
  )
  duplicate ? callback(new Error('Артикул уже занят')) : callback()
}

const rules = {
  formId: [
    { required: true, message: 'Введите артикул', trigger: 'blur' },
    { validator: validateFormId, trigger: 'blur' }
  ],
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }],
  cavities: [{ required: true, message: 'Укажите гнёздность', trigger: 'blur' }],
  maxResourceCycles: [{ required: true, message: 'Укажите ресурс', trigger: 'blur' }],
  productTypeId: [{ required: true, message: 'Выберите тип изделия', trigger: 'change' }]
}

const loadProductTypes = async () => {
  try {
    const { data } = await productTypesApi.getList({ isActive: true })
    productTypes.value = data
  } catch (error) {
    ElMessage.error('Ошибка загрузки типов изделий')
  }
}

const loadImms = async () => {
  try {
    const { data } = await immApi.getList({ isActive: true })
    imms.value = data
  } catch (error) {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}

onMounted(async () => {
  await Promise.all([loadMolds(), loadProductTypes(), loadImms()])
})

let loadMoldsRequestId = 0

onBeforeUnmount(() => {
  loadMoldsRequestId += 1
})

const loadMolds = async () => {
  const requestId = ++loadMoldsRequestId
  loading.value = true
  try {
    const response = await moldsApi.getList({
      isActive: filters.isActive,
      search: filters.search,
      customer: filters.customer || undefined,
      customerEmpty: filters.customerEmpty ? true : undefined
    })
    if (requestId === loadMoldsRequestId) molds.value = response.data
  } catch (error) {
    if (requestId === loadMoldsRequestId) {
      ElMessage.error('Ошибка загрузки пресс-форм')
    }
  } finally {
    if (requestId === loadMoldsRequestId) loading.value = false
  }
}

const showCreateModal = () => {
  editingMold.value = null
  editingArchivedType.value = null
  Object.assign(form, {
    formId: '',
    name: '',
    cavities: 1,
    runnerWeightGrams: 0,
    maxResourceCycles: 0,
    to1Cycles: 0,
    to2Cycles: 0,
    storageLocationIndex: '',
    isActive: true,
    productTypeId: null,
    hasMovingParts: false,
    requiresRobot: false,
    requiresConveyor: false,
    compatibleImmIds: [],
  })
  dialogVisible.value = true
}

const editMold = (mold) => {
  editingMold.value = mold
  editingArchivedType.value = (mold.productTypeId && !productTypes.value.some(pt => pt.id === mold.productTypeId))
    ? { id: mold.productTypeId, article: mold.productTypeArticle, name: mold.productTypeName,
        company: mold.productTypeCompany }
    : null
  Object.assign(form, {
    formId: mold.formId,
    name: mold.name,
    cavities: mold.cavities,
    runnerWeightGrams: mold.runnerWeightGrams,
    maxResourceCycles: mold.maxResourceCycles,
    to1Cycles: mold.to1Cycles,
    to2Cycles: mold.to2Cycles,
    storageLocationIndex: mold.storageLocationIndex,
    isActive: mold.isActive,
    productTypeId: mold.productTypeId ?? null,
    hasMovingParts: mold.hasMovingParts ?? false,
    requiresRobot: mold.requiresRobot ?? false,
    requiresConveyor: mold.requiresConveyor ?? false,
    compatibleImmIds: (mold.compatibleImms ?? []).map(i => i.id),
  })
  dialogVisible.value = true
}

const saveMold = async () => {
  if (!formRef.value) return
  
  await formRef.value.validate(async (valid) => {
    if (!valid) return

    saving.value = true
    try {
      if (editingMold.value) {
        await moldsApi.update(editingMold.value.id, form)
        ElMessage.success('Пресс-форма обновлена')
      } else {
        await moldsApi.create(form)
        ElMessage.success('Пресс-форма создана')
      }

      dialogVisible.value = false
      await loadMolds()
    } catch (error) {
      ElMessage.error(error.response?.data ?? 'Ошибка сохранения пресс-формы')
    } finally {
      saving.value = false
    }
  })
}

const deleteMold = async (mold) => {
  try {
    await ElMessageBox.confirm(`Деактивировать пресс-форму "${mold.name}"?`, 'Подтверждение', {
      type: 'warning'
    })
    
    await moldsApi.update(mold.id, { isActive: false })
    ElMessage.success('Пресс-форма деактивирована')
    await loadMolds()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error(apiErrorMessage(error, 'Ошибка удаления пресс-формы'))
    }
  }
}
</script>