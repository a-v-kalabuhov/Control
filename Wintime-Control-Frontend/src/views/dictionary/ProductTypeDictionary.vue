<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник типов изделий</h2>
        <p class="text-gray-600 mt-1">Номенклатура: типы изделий и их изделия</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить тип изделия
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Артикул или наименование" clearable />
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
          <el-button type="primary" @click="loadItems">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-table :data="items" stripe style="width: 100%" v-loading="loading" row-key="id">
      <el-table-column type="expand">
        <template #default="{ row }">
          <el-table :data="row.products" size="small" class="m-2">
            <el-table-column prop="fullArticle" label="Полный артикул" width="220" />
            <el-table-column label="Наименование">
              <template #default="{ row: p }">
                <div class="flex items-center gap-1">
                  <span>{{ p.name }}</span>
                  <el-tooltip v-if="p.hasSample" content="Есть образец">
                    <el-icon data-test="product-sample" class="text-emerald-600"><Box /></el-icon>
                  </el-tooltip>
                </div>
              </template>
            </el-table-column>
            <el-table-column prop="partWeightGrams" label="Вес, г" width="100" align="right" />
            <el-table-column label="Гнёзда" width="100" align="right">
              <template #default="{ row: p }">{{ p.cavities ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="Цикл литья, с" width="130" align="right">
              <template #default="{ row: p }">{{ p.plannedInjectionCycleSeconds ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="Рецепт" min-width="260">
              <template #default="{ row: p }">
                <div class="flex items-start gap-1">
                  <span class="whitespace-normal">{{ recipeText(p) }}</span>
                  <el-tooltip v-if="!recipeSumOk(p)" :content="recipeSumHint(p.recipe)">
                    <el-tag type="danger" size="small">≠ 100 %</el-tag>
                  </el-tooltip>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="Статус" width="120" align="center">
              <template #default="{ row: p }">
                <el-tag :type="p.isActive ? 'success' : 'info'">{{ p.isActive ? 'Активно' : 'В архиве' }}</el-tag>
              </template>
            </el-table-column>
          </el-table>
        </template>
      </el-table-column>
      <el-table-column prop="article" label="Артикул" width="200" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column label="Изделия" width="110" align="center">
        <template #default="{ row }">
          <span class="products-count">{{ activeProducts(row).length }}</span>
        </template>
      </el-table-column>
      <el-table-column label="Статус" width="120" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активно' : 'В архиве' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="220" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editItem(row)">Редактировать</el-button>
          <el-button size="small" :type="row.isActive ? 'danger' : 'success'" @click="toggleArchive(row)">
            {{ row.isActive ? 'В архив' : 'Вернуть' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="dialogVisible"
      :title="editing ? 'Редактирование типа изделия' : 'Новый тип изделия'"
      width="720px"
    >
      <el-form :model="form" label-width="140px" :rules="rules" ref="formRef">
        <el-form-item label="Артикул" prop="article" required>
          <el-input v-model="form.article" placeholder="ART-001" @input="form.article = form.article.toUpperCase()" />
        </el-form-item>
        <el-form-item label="Наименование" prop="name" required>
          <el-input v-model="form.name" placeholder="Крышка 48мм" />
        </el-form-item>
        <el-form-item label="Заказчик" prop="company">
          <SuggestInput v-model="form.company" :fetch="fetchCompanies" placeholder="Необязательно" />
        </el-form-item>
        <el-form-item label="Тарная единица, шт." prop="packagingUnit">
          <el-input-number
            v-model="form.packagingUnit" :min="1" :max="1000000" :precision="0"
            data-test="type-packaging-unit"
            class="w-full" controls-position="right"
          />
          <div class="text-xs text-gray-500 mt-1">Необязательно; копируется в новые задания</div>
        </el-form-item>
      </el-form>

      <div v-if="editing" class="mt-4">
        <div class="mb-2 flex items-center justify-between">
          <h3 class="font-semibold text-gray-800">Изделия</h3>
          <el-button size="small" type="primary" @click="showProductModal">Добавить изделие</el-button>
        </div>
        <el-table :data="editing.products" size="small">
          <el-table-column prop="article" label="Артикул" width="100" />
          <el-table-column prop="fullArticle" label="Полный артикул" width="150" />
          <el-table-column label="Наименование">
            <template #default="{ row: p }">
              <div class="flex items-center gap-1">
                <span>{{ p.name }}</span>
                <el-tooltip v-if="p.hasSample" content="Есть образец">
                  <el-icon data-test="product-sample" class="text-emerald-600"><Box /></el-icon>
                </el-tooltip>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="Статус" width="100" align="center">
            <template #default="{ row: p }">
              <el-tag :type="p.isActive ? 'success' : 'info'">{{ p.isActive ? 'Активно' : 'В архиве' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="Действия" width="320">
            <template #default="{ row: p }">
              <el-button class="product-recipe" size="small" @click="openRecipe(p)">Рецепт</el-button>
              <el-button size="small" @click="editProduct(p)">Ред.</el-button>
              <el-button size="small" :type="p.isActive ? 'danger' : 'success'" @click="toggleProductArchive(p)">
                {{ p.isActive ? 'В архив' : 'Вернуть' }}
              </el-button>
              <el-button v-if="!p.isUsed" class="product-delete" size="small" type="danger" plain @click="deleteProduct(p)">
                Удалить
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <ProductRecipeDialog
          v-model="recipeDialogVisible"
          :type-id="editing?.id"
          :product="recipeProduct"
          @saved="onRecipeSaved"
        />
      </div>

      <template #footer>
        <el-button @click="dialogVisible = false">{{ editing ? 'Закрыть' : 'Отмена' }}</el-button>
        <el-button type="primary" @click="save" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="productDialogVisible"
      :title="editingProduct ? 'Редактирование изделия' : 'Новое изделие'"
      width="460px"
      append-to-body
    >
      <el-form :model="productForm" label-width="120px">
        <el-form-item label="Артикул" required>
          <div class="w-full">
            <el-input v-model="productForm.article" placeholder="BLK" @input="onProductArticleInput" />
            <div class="text-xs text-gray-500 mt-1">Полный артикул: {{ fullArticlePreview }}</div>
          </div>
        </el-form-item>
        <el-form-item label="Наименование" required>
          <el-input v-model="productForm.name" placeholder="Чёрный" />
        </el-form-item>
        <el-form-item label="Вес детали, г">
          <el-input-number
            v-model="productForm.partWeightGrams" :min="0" :precision="2" :step="0.1"
            :value-on-clear="0" class="w-full" controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Гнёзда">
          <el-input-number
            v-model="productForm.cavities" :min="1" :precision="0"
            data-test="product-cavities" class="w-full" controls-position="right"
          />
          <div class="text-xs text-gray-500 mt-1">Необязательно; если пусто — максимум выбранной ПФ</div>
        </el-form-item>
        <el-form-item label="Цикл литья, с">
          <el-input-number
            v-model="productForm.plannedInjectionCycleSeconds" :min="1" :max="86400"
            class="w-full" controls-position="right"
          />
          <div class="text-xs text-gray-500 mt-1">Необязательно; полный цикл задаётся в задании</div>
        </el-form-item>
        <el-form-item label="Образец">
          <el-switch v-model="productForm.hasSample" active-text="Есть образец" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="productDialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="saveProduct" :loading="savingProduct">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Box } from '@element-plus/icons-vue'
import { productTypesApi } from '@/api/productTypes'
import { activeProducts } from '@/utils/productLabel'
import { nextProductArticle } from '@/utils/productArticle'
import { apiErrorMessage } from '@/utils/apiError'
import ProductRecipeDialog from './ProductRecipeDialog.vue'
import SuggestInput from '@/components/common/SuggestInput.vue'
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'
import { formatRecipe, isRecipeSumOk, recipeSumHint as recipeSumHintText } from '@/utils/recipe'

// Текст ошибки сервера; нестроковое тело (ProblemDetails и т.п.) заменяется запасным текстом.
function errText(error, fallback) {
  const msg = apiErrorMessage(error, fallback)
  return typeof msg === 'string' && msg ? msg : fallback
}

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref(null)
const formRef = ref(null)
const items = ref([])

// BL-36: рецепт изделия — отдельный диалог; после сохранения перечитываем тип.
const recipeDialogVisible = ref(false)
const recipeProduct = ref(null)

function recipeText(product) {
  return formatRecipe(product?.recipe) || '—'
}

function recipeSumOk(product) {
  return isRecipeSumOk(product?.recipe)
}

function recipeSumHint(productRecipe) {
  return recipeSumHintText(productRecipe)
}

function openRecipe(product) {
  recipeProduct.value = product
  recipeDialogVisible.value = true
}

async function onRecipeSaved(context) {
  if (editing.value?.id === context.typeId) {
    await safeReloadEditing()
    return
  }
  // Сохранение могло завершиться после закрытия A или открытия B: обновляем только список.
  await loadItems()
}

const productDialogVisible = ref(false)
const savingProduct = ref(false)
const editingProduct = ref(null)
const productForm = reactive({
  article: '', name: '', partWeightGrams: 0, cavities: null, plannedInjectionCycleSeconds: null, hasSample: false
})

const filters = reactive({ search: '', isActive: null, customer: null, customerEmpty: false })
const form = reactive({ article: '', name: '', company: '', packagingUnit: null })

const rules = {
  article: [{ required: true, message: 'Введите артикул', trigger: 'blur' }],
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }]
}

const fullArticlePreview = computed(() => `${editing.value?.article ?? ''}.${productForm.article || '…'}`)

onMounted(loadItems)

let loadItemsRequestId = 0

onBeforeUnmount(() => {
  loadItemsRequestId += 1
})

async function loadItems() {
  const requestId = ++loadItemsRequestId
  loading.value = true
  try {
    const { data } = await productTypesApi.getList({
      isActive: filters.isActive,
      search: filters.search,
      customer: filters.customer || undefined,
      customerEmpty: filters.customerEmpty ? true : undefined
    })
    if (requestId === loadItemsRequestId) items.value = data
  } catch {
    if (requestId === loadItemsRequestId) {
      ElMessage.error('Ошибка загрузки типов изделий')
    }
  } finally {
    if (requestId === loadItemsRequestId) loading.value = false
  }
}

async function fetchCompanies(search) {
  const { data } = await productTypesApi.suggestCompanies(search)
  return data
}

function showCreateModal() {
  editing.value = null
  Object.assign(form, { article: '', name: '', company: '', packagingUnit: null })
  dialogVisible.value = true
}

function editItem(row) {
  editing.value = row
  Object.assign(form, {
    article: row.article, name: row.name, company: row.company ?? '', packagingUnit: row.packagingUnit ?? null
  })
  dialogVisible.value = true
}

async function save() {
  if (!formRef.value) return
  await formRef.value.validate(async (valid) => {
    if (!valid) return
    saving.value = true
    try {
      if (editing.value) {
        await productTypesApi.update(editing.value.id, {
          ...form,
          clearPackagingUnit: form.packagingUnit === null || form.packagingUnit === undefined
        })
        ElMessage.success('Тип изделия обновлён')
        dialogVisible.value = false
      } else {
        const { data } = await productTypesApi.create(form)
        // Окно остаётся открытым в режиме редактирования: у типа уже есть изделие «0».
        editing.value = data
        ElMessage.success('Тип изделия создан')
      }
      await loadItems()
    } catch (error) {
      ElMessage.error(errText(error, 'Ошибка сохранения типа изделия'))
    } finally {
      saving.value = false
    }
  })
}

async function toggleArchive(row) {
  try {
    await productTypesApi.update(row.id, { isActive: !row.isActive })
    ElMessage.success(row.isActive ? 'Тип изделия в архиве' : 'Тип изделия возвращён')
    await loadItems()
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка изменения статуса'))
  }
}

function onProductArticleInput() {
  productForm.article = productForm.article.toUpperCase()
}

function showProductModal() {
  editingProduct.value = null
  Object.assign(productForm, {
    article: nextProductArticle(editing.value.products),
    name: '', partWeightGrams: 0, cavities: null, plannedInjectionCycleSeconds: null, hasSample: false
  })
  productDialogVisible.value = true
}

function editProduct(p) {
  editingProduct.value = p
  Object.assign(productForm, {
    article: p.article, name: p.name,
    partWeightGrams: p.partWeightGrams ?? 0,
    cavities: p.cavities ?? null,
    plannedInjectionCycleSeconds: p.plannedInjectionCycleSeconds ?? null,
    hasSample: p.hasSample ?? false
  })
  productDialogVisible.value = true
}

// Изменения изделий сохраняются сразу отдельными запросами, не по «Сохранить» типа.
async function reloadEditing() {
  const editingId = editing.value.id
  const { data } = await productTypesApi.getById(editingId)
  if (editing.value?.id === editingId) {
    editing.value = data
  }
  await loadItems()
}

// Перезагрузка после успешного сохранения: её сбой — не ошибка самого сохранения.
async function safeReloadEditing() {
  try {
    await reloadEditing()
  } catch {
    ElMessage.error('Не удалось обновить данные типа')
  }
}

async function saveProduct() {
  const article = productForm.article.trim().toUpperCase()
  const name = productForm.name.trim()
  if (!article || !name) {
    ElMessage.error('Введите артикул и наименование изделия')
    return
  }
  savingProduct.value = true
  try {
    const typeId = editing.value.id
    const payload = {
      article, name,
      partWeightGrams: productForm.partWeightGrams ?? 0,
      cavities: productForm.cavities ?? null,
      plannedInjectionCycleSeconds: productForm.plannedInjectionCycleSeconds ?? null,
      hasSample: productForm.hasSample ?? false
    }
    if (editingProduct.value) {
      await productTypesApi.updateProduct(typeId, editingProduct.value.id, {
        ...payload,
        clearCavities: payload.cavities === null,
        clearPlannedInjectionCycleSeconds: payload.plannedInjectionCycleSeconds === null
      })
      ElMessage.success('Изделие обновлено')
    } else {
      await productTypesApi.addProduct(typeId, payload)
      ElMessage.success('Изделие добавлено')
    }
    productDialogVisible.value = false
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка сохранения изделия'))
    return
  } finally {
    savingProduct.value = false
  }
  await safeReloadEditing()
}

async function toggleProductArchive(p) {
  try {
    await productTypesApi.updateProduct(editing.value.id, p.id, { isActive: !p.isActive })
    ElMessage.success(p.isActive ? 'Изделие в архиве' : 'Изделие возвращено')
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка изменения статуса'))
    return
  }
  await safeReloadEditing()
}

async function deleteProduct(p) {
  try {
    await ElMessageBox.confirm(`Удалить изделие ${p.fullArticle}?`, 'Подтверждение', {
      confirmButtonText: 'Удалить', cancelButtonText: 'Отмена', type: 'warning'
    })
  } catch {
    return
  }
  try {
    await productTypesApi.deleteProduct(editing.value.id, p.id)
    ElMessage.success('Изделие удалено')
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка удаления изделия'))
    return
  }
  await safeReloadEditing()
}
</script>
