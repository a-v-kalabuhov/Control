<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Заказы</h2>
        <p class="text-gray-600 mt-1">Учёт производственных заказов и прогресса их выполнения</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Создать заказ
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Номер заказа" clearable @keyup.enter="loadOrders" />
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

    <el-table :data="orders" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="number" label="Номер" width="140" />
      <el-table-column label="Дата" width="120">
        <template #default="{ row }">{{ formatDate(row.date) }}</template>
      </el-table-column>
      <el-table-column label="Срок" width="120">
        <template #default="{ row }">{{ formatDate(row.dueDate) }}</template>
      </el-table-column>
      <el-table-column prop="productTypeArticle" label="Изделие" />
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
          <el-button size="small" @click="openOrder(row)">Открыть</el-button>
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
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editingId ? 'Редактирование заказа' : 'Новый заказ'" width="500px">
      <el-form :model="form" label-width="140px" :rules="rules" ref="formRef">
        <el-form-item label="Номер" prop="number" required>
          <el-input v-model="form.number" placeholder="ORD-001" />
        </el-form-item>
        <el-form-item label="Дата" prop="date" required>
          <el-date-picker
            v-model="form.date"
            type="date"
            placeholder="Выберите дату"
            format="DD.MM.YYYY"
            value-format="YYYY-MM-DD"
            class="w-full"
          />
        </el-form-item>
        <el-form-item label="Срок" prop="dueDate" required>
          <el-date-picker
            v-model="form.dueDate"
            type="date"
            placeholder="Выберите дату"
            format="DD.MM.YYYY"
            value-format="YYYY-MM-DD"
            class="w-full"
          />
        </el-form-item>
        <el-form-item label="Тип изделия" prop="productTypeId" required>
          <el-select
            v-model="form.productTypeId"
            filterable
            placeholder="Выберите тип изделия"
            class="w-full"
            :loading="productTypesLoading"
          >
            <el-option
              v-for="pt in productTypes"
              :key="pt.id"
              :label="pt.article"
              :value="pt.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Количество" prop="quantity" required>
          <el-input-number v-model="form.quantity" :min="1" class="w-full" controls-position="right" />
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
import { ref, reactive, onMounted, watch } from 'vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { ordersApi } from '@/api/orders'
import { productTypesApi } from '@/api/productTypes'
import { ORDER_STATUS_KEYS, getOrderStatusMeta } from '@/constants/orderStatus'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingId = ref(null)
const formRef = ref(null)
const orders = ref([])
const productTypes = ref([])
const productTypesLoading = ref(false)

const filters = reactive({ status: null, search: '' })
const form = reactive({
  number: '',
  date: '',
  dueDate: '',
  productTypeId: null,
  quantity: 1,
  note: ''
})

const rules = {
  number: [{ required: true, message: 'Введите номер заказа', trigger: 'blur' }],
  date: [{ required: true, message: 'Укажите дату', trigger: 'change' }],
  dueDate: [{ required: true, message: 'Укажите срок', trigger: 'change' }],
  productTypeId: [{ required: true, message: 'Выберите тип изделия', trigger: 'change' }],
  quantity: [{ required: true, message: 'Укажите количество', trigger: 'change' }]
}

onMounted(loadOrders)

// Сбрасываем режим редактирования при закрытии модалки (крестик/Esc/клик вне окна),
// чтобы следующее открытие «Создать заказ» не унаследовало editingId.
watch(dialogVisible, (visible) => {
  if (!visible) editingId.value = null
})

function progressPercentage(row) {
  return Math.min(100, Math.round(row.progressPercent))
}

function formatDate(date) {
  return date ? dayjs(date).format('DD.MM.YYYY') : ''
}

async function loadOrders() {
  loading.value = true
  try {
    const { data } = await ordersApi.getList({
      status: filters.status,
      search: filters.search
    })
    orders.value = data
  } catch (error) {
    ElMessage.error(error.response?.data ?? 'Ошибка загрузки заказов')
  } finally {
    loading.value = false
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

function showCreateModal() {
  editingId.value = null
  Object.assign(form, { number: '', date: '', dueDate: '', productTypeId: null, quantity: 1, note: '' })
  dialogVisible.value = true
  loadProductTypes()
}

function editItem(row) {
  editingId.value = row.id
  Object.assign(form, {
    number: row.number ?? '',
    date: row.date ?? '',
    dueDate: row.dueDate ?? '',
    productTypeId: row.productTypeId ?? null,
    quantity: row.quantity ?? 1,
    note: row.note ?? ''
  })
  dialogVisible.value = true
  loadProductTypes()
}

async function save() {
  if (!formRef.value) return
  await formRef.value.validate(async (valid) => {
    if (!valid) return
    saving.value = true
    try {
      if (editingId.value) {
        await ordersApi.update(editingId.value, form)
        ElMessage.success('Заказ обновлён')
      } else {
        await ordersApi.create(form)
        ElMessage.success('Заказ создан')
      }
      dialogVisible.value = false
      await loadOrders()
    } catch (error) {
      ElMessage.error(error.response?.data ?? (editingId.value ? 'Ошибка обновления заказа' : 'Ошибка создания заказа'))
    } finally {
      saving.value = false
    }
  })
}

function openOrder(row) {
  // Открытие карточки заказа реализуется в Task 11 (модалка с деталями/задачами).
  ElMessage.info(`Заказ ${row.number}`)
}

async function completeOrder(row) {
  try {
    await ordersApi.complete(row.id)
    ElMessage.success('Заказ завершён')
    await loadOrders()
  } catch (error) {
    ElMessage.error(error.response?.data ?? 'Ошибка завершения заказа')
  }
}

async function cancelOrder(row) {
  try {
    await ordersApi.cancel(row.id)
    ElMessage.success('Заказ отменён')
    await loadOrders()
  } catch (error) {
    ElMessage.error(error.response?.data ?? 'Ошибка отмены заказа')
  }
}

async function reopenOrder(row) {
  try {
    await ordersApi.reopen(row.id)
    ElMessage.success('Заказ возобновлён')
    await loadOrders()
  } catch (error) {
    ElMessage.error(error.response?.data ?? 'Ошибка возобновления заказа')
  }
}
</script>
