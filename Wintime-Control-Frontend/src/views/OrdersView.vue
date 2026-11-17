<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Заказы</h2>
        <p class="text-gray-600 mt-1">Учёт производственных заказов и прогресса их выполнения</p>
      </div>
      <el-button v-if="canEdit" type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Создать заказ
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Номер заказа" clearable @keyup.enter="loadOrders" />
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
          <el-select v-model="filters.status" placeholder="Все" clearable style="width: 180px;">
            <el-option v-for="key in ORDER_STATUS_KEYS" :key="key" :label="getOrderStatusMeta(key).label" :value="key" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadOrders">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <OrderDetailModal v-model="showDetail" :order-id="selectedOrderId" @updated="loadOrders" />

    <el-table :data="sortedOrders" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="number" label="Номер" width="140">
        <template #header><SortableColumnHeader label="Номер" field="number" :sort="sort" @sort="handleSort" /></template>
      </el-table-column>
      <el-table-column label="Дата" width="120">
        <template #header><SortableColumnHeader label="Дата" field="orderDate" :sort="sort" @sort="handleSort" /></template>
        <template #default="{ row }">{{ formatDate(row.orderDate) }}</template>
      </el-table-column>
      <el-table-column label="Завершить до" width="130">
        <template #header><SortableColumnHeader label="Завершить до" field="dueDate" :sort="sort" @sort="handleSort" /></template>
        <template #default="{ row }">{{ formatDate(row.dueDate) }}</template>
      </el-table-column>
      <el-table-column prop="productFullArticle" label="Изделие" min-width="180">
        <template #header><SortableColumnHeader label="Изделие" field="productFullArticle" :sort="sort" @sort="handleSort" /></template>
        <template #default="{ row }">
          <div>{{ row.productFullArticle }}</div>
          <div v-if="productSecondary(row.productTypeName, row.productName)" class="text-xs text-gray-500">
            {{ productSecondary(row.productTypeName, row.productName) }}
          </div>
        </template>
      </el-table-column>
      <el-table-column prop="customer" label="Заказчик" min-width="160">
        <template #header><SortableColumnHeader label="Заказчик" field="customer" :sort="sort" @sort="handleSort" /></template>
      </el-table-column>
      <el-table-column prop="quantity" label="Заказано" width="110" align="center" />
      <el-table-column label="Прогресс" width="200">
        <template #default="{ row }">
          <el-progress :percentage="progressPercentage(row)" />
          <div class="text-xs text-gray-500 mt-1">{{ row.goodQuantity }} / {{ row.quantity }}</div>
        </template>
      </el-table-column>
      <el-table-column label="Статус" width="140" align="center">
        <template #default="{ row }">
          <span
            class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
            :class="`${getOrderStatusMeta(row.status).bg} ${getOrderStatusMeta(row.status).text}`"
          >
            {{ getOrderStatusMeta(row.status).label }}
          </span>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="280" fixed="right">
        <template #default="{ row }">
          <div class="order-actions">
            <el-button size="small" @click="openOrder(row)">Открыть</el-button>
            <template v-if="canEdit">
              <el-button size="small" @click="editItem(row)">Редактировать</el-button>
              <el-button
                size="small"
                type="success"
                :disabled="row.goodQuantity < row.quantity"
                @click="completeOrder(row)"
              >
                Завершить
              </el-button>
              <el-button
                v-if="row.status !== 'Active'"
                size="small"
                @click="reopenOrder(row)"
              >
                Возобновить
              </el-button>
              <el-button
                v-else
                size="small"
                type="danger"
                @click="cancelOrder(row)"
              >
                Отменить
              </el-button>
            </template>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editingId ? 'Редактирование заказа' : 'Новый заказ'" width="600px">
      <el-form :model="form" label-width="140px" :rules="rules" ref="formRef">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="Номер" prop="number" required>
              <el-input v-model="form.number" placeholder="ORD-001" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Дата" prop="orderDate">
              <el-date-picker
                v-model="form.orderDate"
                type="date"
                placeholder="Необязательно"
                clearable
                format="DD.MM.YYYY"
                value-format="YYYY-MM-DD"
                class="w-full"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="Тип изделия" prop="productTypeId" required>
          <el-select
            v-model="form.productTypeId"
            filterable
            :filter-method="filterProductTypes"
            placeholder="Выберите тип изделия"
            class="w-full"
            :loading="productTypesLoading"
          >
            <el-option
              v-for="pt in filteredProductTypes"
              :key="pt.id"
              :label="`${pt.article} — ${pt.name}`"
              :value="pt.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Изделие" prop="productId" required>
          <el-select
            v-model="form.productId"
            filterable
            placeholder="Выберите изделие"
            class="w-full"
            :disabled="!form.productTypeId"
          >
            <el-option
              v-for="p in productOptions"
              :key="p.id"
              :label="`${p.fullArticle} — ${p.name}`"
              :value="p.id"
              :disabled="!p.isActive"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Заказчик" prop="customer">
          <SuggestInput v-model="form.customer" :fetch="fetchCompanies" placeholder="Необязательно" />
        </el-form-item>
        <el-form-item label="Количество" prop="quantity" required>
          <el-input-number v-model="form.quantity" :min="1" class="w-full" controls-position="right" />
        </el-form-item>
        <el-form-item label="Завершить до" prop="dueDate" required>
          <el-date-picker
            v-model="form.dueDate"
            type="date"
            placeholder="Выберите дату"
            format="DD.MM.YYYY"
            value-format="YYYY-MM-DD"
            class="w-full"
          />
        </el-form-item>
        <el-form-item label="Примечание">
          <el-input v-model="form.note" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="save" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, nextTick, onMounted, onBeforeUnmount, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { ordersApi } from '@/api/orders'
import { productTypesApi } from '@/api/productTypes'
import { ORDER_STATUS_KEYS, getOrderStatusMeta } from '@/constants/orderStatus'
import OrderDetailModal from '@/components/orders/OrderDetailModal.vue'
import { apiErrorMessage } from '@/utils/apiError'
import { activeProducts, productSecondary } from '@/utils/productLabel'
import SuggestInput from '@/components/common/SuggestInput.vue'
import SortableColumnHeader from '@/components/common/SortableColumnHeader.vue'
import { useTableSort } from '@/utils/tableSort'
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'

const authStore = useAuthStore()
// Observer — только чтение (PZP-07a); запись защищена на бэкенде
const canEdit = computed(() => authStore.isManager || authStore.isAdmin)

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const showDetail = ref(false)
const selectedOrderId = ref(null)
const editingId = ref(null)
const editingRow = ref(null)
const formRef = ref(null)
const orders = ref([])
const { sort, handleSort, sortedItems } = useTableSort('orders_sort_v1')
const sortedOrders = sortedItems(orders)
const productTypes = ref([])
const productTypesLoading = ref(false)
const productTypeFilterQuery = ref('')

const filteredProductTypes = computed(() => {
  const query = productTypeFilterQuery.value.trim().toLowerCase()
  if (!query) return productTypes.value
  return productTypes.value.filter(pt =>
    (pt.article ?? '').toLowerCase().includes(query)
    || (pt.name ?? '').toLowerCase().includes(query))
})

const filters = reactive({ status: null, search: '', customer: null, customerEmpty: false })
const form = reactive({
  number: '',
  customer: '',
  orderDate: '',
  dueDate: '',
  productTypeId: null,
  productId: null,
  quantity: 1,
  note: ''
})

const rules = {
  number: [{ required: true, message: 'Введите номер заказа', trigger: 'blur' }],
  dueDate: [{ required: true, message: 'Укажите, до какой даты завершить заказ', trigger: 'change' }],
  productTypeId: [{ required: true, message: 'Выберите тип изделия', trigger: 'change' }],
  productId: [{ required: true, message: 'Выберите изделие', trigger: 'change' }],
  quantity: [{ required: true, message: 'Укажите количество', trigger: 'change' }]
}

// Изделия выбранного типа: только активные (архивные нельзя назначить в новый заказ).
// При редактировании изделие заказа могло быть архивировано позже — добавляем его
// отдельной неактивной опцией, иначе select покажет сырой GUID.
const productOptions = computed(() => {
  const list = activeProducts(productTypes.value.find((t) => t.id === form.productTypeId))
  const row = editingRow.value
  if (row?.productId && form.productId === row.productId && !list.some((p) => p.id === row.productId)) {
    return [
      ...list,
      {
        id: row.productId,
        fullArticle: row.productFullArticle || row.productId,
        name: row.productName,
        isActive: false
      }
    ]
  }
  return list
})

// При открытии на редактирование тип и изделие выставляются разом — watch не должен сбросить изделие.
let populating = false

watch(() => form.productTypeId, (id, old) => {
  if (populating) return
  if (id !== old) form.productId = null
  const company = productTypes.value.find((type) => type.id === id)?.company
  if (company) form.customer = company
  const list = productOptions.value
  if (list.length === 1) form.productId = list[0].id
})

onMounted(loadOrders)

// Сбрасываем режим редактирования при закрытии модалки (крестик/Esc/клик вне окна),
// чтобы следующее открытие «Создать заказ» не унаследовало editingId.
watch(dialogVisible, (visible) => {
  if (!visible) {
    editingId.value = null
    editingRow.value = null
    productTypeFilterQuery.value = ''
  }
})

function progressPercentage(row) {
  return Math.min(100, Math.round(row.progressPercent))
}

function formatDate(date) {
  return date ? dayjs(date).format('DD.MM.YYYY') : ''
}

let loadOrdersRequestId = 0

onBeforeUnmount(() => {
  loadOrdersRequestId += 1
})

async function loadOrders() {
  const requestId = ++loadOrdersRequestId
  loading.value = true
  try {
    const { data } = await ordersApi.getList({
      status: filters.status,
      search: filters.search,
      customer: filters.customer || undefined,
      customerEmpty: filters.customerEmpty ? true : undefined
    })
    if (requestId === loadOrdersRequestId) orders.value = data
  } catch (error) {
    if (requestId === loadOrdersRequestId) {
      ElMessage.error(apiErrorMessage(error, 'Ошибка загрузки заказов'))
    }
  } finally {
    if (requestId === loadOrdersRequestId) loading.value = false
  }
}

async function loadProductTypes() {
  productTypesLoading.value = true
  try {
    const { data } = await productTypesApi.getList({ isActive: true })
    productTypes.value = data
  } catch {
    ElMessage.error('Ошибка загрузки типов изделий')
  } finally {
    productTypesLoading.value = false
  }
}

function filterProductTypes(query) {
  productTypeFilterQuery.value = query
}

async function fetchCompanies(search) {
  const { data } = await productTypesApi.suggestCompanies(search)
  return data
}

function showCreateModal() {
  editingId.value = null
  editingRow.value = null
  Object.assign(form, {
    number: '', customer: '', orderDate: '', dueDate: '',
    productTypeId: null, productId: null, quantity: 1, note: ''
  })
  dialogVisible.value = true
  loadProductTypes()
}

function editItem(row) {
  editingId.value = row.id
  editingRow.value = row
  populating = true
  Object.assign(form, {
    number: row.number ?? '',
    customer: row.customer ?? '',
    orderDate: row.orderDate ?? '',
    dueDate: row.dueDate ?? '',
    productTypeId: row.productTypeId ?? null,
    productId: row.productId ?? null,
    quantity: row.quantity ?? 1,
    note: row.note ?? ''
  })
  nextTick(() => { populating = false })
  dialogVisible.value = true
  loadProductTypes()
}

async function save() {
  if (!formRef.value) return
  await formRef.value.validate(async (valid) => {
    if (!valid) return
    saving.value = true
    try {
      // Пустая дата — null (а не ''), иначе бэкенд не разберёт DateTime?
      // Тип нужен только для выбора изделия — на бэкенд уходит productId
      const { productTypeId, ...rest } = form
      const payload = { ...rest, orderDate: form.orderDate || null }
      if (editingId.value) {
        // null в orderDate для update значит «не менять» — очистку передаём явно
        await ordersApi.update(editingId.value, { ...payload, clearOrderDate: !payload.orderDate })
        ElMessage.success('Заказ обновлён')
      } else {
        await ordersApi.create(payload)
        ElMessage.success('Заказ создан')
      }
      dialogVisible.value = false
      await loadOrders()
    } catch (error) {
      ElMessage.error(apiErrorMessage(error, editingId.value ? 'Ошибка обновления заказа' : 'Ошибка создания заказа'))
    } finally {
      saving.value = false
    }
  })
}

function openOrder(row) {
  selectedOrderId.value = row.id
  showDetail.value = true
}

async function completeOrder(row) {
  try {
    await ordersApi.complete(row.id)
    ElMessage.success('Заказ завершён')
    await loadOrders()
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка завершения заказа'))
  }
}

async function cancelOrder(row) {
  try {
    await ordersApi.cancel(row.id)
    ElMessage.success('Заказ отменён')
    await loadOrders()
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка отмены заказа'))
  }
}

async function reopenOrder(row) {
  try {
    await ordersApi.reopen(row.id)
    ElMessage.success('Заказ возобновлён')
    await loadOrders()
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка возобновления заказа'))
  }
}
</script>

<style scoped>
.order-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-start;
  gap: 6px;
}

/* сбрасываем отступы Element Plus между соседними кнопками — расстояние задаёт gap */
.order-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.order-actions :deep(.el-button) {
  width: 118px;
  margin: 0;
}
</style>
