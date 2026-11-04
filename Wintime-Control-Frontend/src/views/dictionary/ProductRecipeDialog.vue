<template>
  <el-dialog
    v-model="visible"
    :title="title"
    width="760px"
    :close-on-click-modal="false"
    append-to-body
  >
    <el-table :data="rows" size="small" empty-text="Рецепт не задан">
      <el-table-column label="Тип" width="120">
        <template #default="{ row }">
          {{ row.materialId ? materialTypeLabel(row.materialType) : '—' }}
        </template>
      </el-table-column>
      <el-table-column label="Материал" min-width="280">
        <template #default="{ row }">
          <el-select
            v-model="row.materialId"
            filterable
            placeholder="Выберите материал"
            class="w-full"
            @focus="loadMaterials"
            @change="onMaterialChange(row)"
          >
            <el-option
              v-for="m in materialOptions(row)"
              :key="m.id"
              :label="materialLabel(m)"
              :value="m.id"
            />
          </el-select>
          <el-tag v-if="isArchived(row)" size="small" type="info" class="mt-1">в архиве</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Процент" width="150">
        <template #default="{ row }">
          <el-input-number
            v-model="row.percent"
            :min="0.01"
            :max="100"
            :step="0.01"
            :precision="2"
            controls-position="right"
          />
        </template>
      </el-table-column>
      <el-table-column width="60">
        <template #default="{ $index }">
          <el-button link type="danger" class="recipe-remove" @click="rows.splice($index, 1)">✕</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="mt-3 flex items-center justify-between">
      <el-button size="small" class="recipe-add" :disabled="!canAddRow" @click="addRow">
        Добавить ингредиент
      </el-button>
      <span class="text-sm" :class="sumClass">{{ sumText }}</span>
    </div>

    <el-alert
      v-if="noPlastic"
      type="error"
      :closable="false"
      show-icon
      class="mt-3"
      title="В рецепте должен быть хотя бы один пластик"
    />

    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button class="recipe-save" type="primary" :disabled="!canSave" :loading="saving" @click="save">
        Сохранить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { productTypesApi } from '@/api/productTypes'
import { materialsApi } from '@/api/materials'
import { apiErrorMessage } from '@/utils/apiError'
import { materialTypeLabel, plasticSum, formatPercent, isRecipeSumOk } from '@/utils/recipe'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  typeId: { type: String, default: null },
  product: { type: Object, default: null }
})

const emit = defineEmits(['update:modelValue', 'saved'])

const visible = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
})

const MAX_ROWS = 8

const rows = ref([])
const materials = ref([])
const saving = ref(false)

const title = computed(() => (props.product
  ? `Рецепт ${props.product.fullArticle} — ${props.product.name}`
  : 'Рецепт'))

// Открытие/смена изделия — строки из рецепта изделия.
watch(() => [props.modelValue, props.product], () => {
  if (!props.modelValue) return
  rows.value = (props.product?.recipe ?? []).map(r => ({
    materialId: r.materialId,
    materialType: r.materialType,
    materialArticle: r.materialArticle,
    materialName: r.materialName,
    materialIsActive: r.materialIsActive,
    percent: Number(r.percent)
  }))
}, { immediate: true })

const canAddRow = computed(() => rows.value.length < MAX_ROWS)

const selectedRows = computed(() => rows.value.filter(r => r.materialId))

const noPlastic = computed(() =>
  selectedRows.value.length > 0 && !selectedRows.value.some(r => r.materialType === 'Plastic'))

const sumIsOk = computed(() => isRecipeSumOk(rows.value))
const sumText = computed(() => {
  const sum = plasticSum(rows.value)
  const base = `Пластики: ${formatPercent(sum)} %`
  return sumIsOk.value ? base : `${base} — сумма пластиков отличается от 100 %`
})
const sumClass = computed(() => (sumIsOk.value ? 'text-gray-500' : 'text-red-600 font-medium'))

function isValidPercent(value) {
  return typeof value === 'number' && Number.isFinite(value) &&
    value >= 0.01 && value <= 100 &&
    Math.abs(value * 100 - Math.round(value * 100)) < 1e-8
}

const hasInvalidPercent = computed(() => selectedRows.value.some(r => !isValidPercent(r.percent)))
const canSave = computed(() => !noPlastic.value && !hasInvalidPercent.value)

async function loadMaterials() {
  if (materials.value.length > 0) return
  try {
    const { data } = await materialsApi.getList({ isActive: true })
    materials.value = data
  } catch {
    ElMessage.error('Ошибка загрузки материалов')
  }
}

// Активные материалы + материал самой строки (архивный тоже — иначе селект покажет GUID).
function materialOptions(row) {
  const taken = new Set(rows.value.filter(r => r !== row).map(r => r.materialId))
  const options = materials.value.filter(m => !taken.has(m.id))
  if (row.materialId && !options.some(m => m.id === row.materialId)) {
    options.push({
      id: row.materialId,
      type: row.materialType,
      article: row.materialArticle,
      name: row.materialName,
      isActive: row.materialIsActive
    })
  }
  return options
}

function materialLabel(m) {
  return [m.article, m.name].filter(Boolean).join(' — ')
}

function isArchived(row) {
  const known = materials.value.find(m => m.id === row.materialId)
  if (known) return !known.isActive
  return row.materialIsActive === false
}

// Тип берётся из материала и только читается — его нельзя выбрать руками.
function onMaterialChange(row) {
  const material = materials.value.find(m => m.id === row.materialId)
  if (!material) return
  row.materialType = material.type
  row.materialArticle = material.article
  row.materialName = material.name
  row.materialIsActive = material.isActive
}

function addRow() {
  if (!canAddRow.value) return
  rows.value.push({ materialId: null, materialType: null, percent: 10 })
}

let saveGeneration = 0

async function save() {
  if (!canSave.value) return
  const generation = ++saveGeneration
  const typeId = props.typeId
  const productId = props.product.id
  saving.value = true
  try {
    const ingredients = selectedRows.value
      .map(r => ({ materialId: r.materialId, percent: Number(r.percent) }))
    await productTypesApi.setRecipe(typeId, productId, ingredients)
    ElMessage.success('Рецепт сохранён')
    emit('saved', { typeId, productId })
    // Пока запрос шёл, диалог могли закрыть и открыть для другого изделия.
    if (props.modelValue && props.typeId === typeId && props.product?.id === productId) {
      visible.value = false
    }
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка сохранения рецепта'))
  } finally {
    if (generation === saveGeneration) saving.value = false
  }
}
</script>

<style scoped>
</style>
