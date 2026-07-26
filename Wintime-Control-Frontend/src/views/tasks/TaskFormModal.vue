<template>
  <el-dialog
    v-model="visible"
    :title="editingTask ? 'Редактирование задания' : 'Новое задание'"
    width="700px"
    :close-on-click-modal="false"
    @closed="onClosed"
  >
    <el-form 
      ref="formRef"
      :model="form" 
      :rules="rules" 
      label-width="150px"
      label-position="left"
    >
      <el-form-item label="Оборудование (ТПА)" prop="immId">
        <el-select 
          v-model="form.immId" 
          placeholder="Выберите ТПА" 
          class="w-full"
          @focus="loadImms"
          :disabled="editingTask"
        >
          <el-option
            v-for="imm in imms"
            :key="imm.id"
            :label="`${imm.name} (${imm.manufacturer} ${imm.model})`"
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
          filterable
        >
          <el-option
            v-for="mold in molds"
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

      <el-form-item label="Заказ">
        <el-select
          v-model="form.orderId"
          placeholder="Выберите заказ"
          class="w-full"
          filterable
          :clearable="false"
          :disabled="!selectedProductTypeId"
        >
          <el-option
            v-for="order in orderOptions"
            :key="order.id"
            :label="`${order.number}${order.productTypeArticle ? ' (' + order.productTypeArticle + ')' : ''}`"
            :value="order.id"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="Наладчик" prop="personnelId">
        <el-select 
          v-model="form.personnelId" 
          placeholder="Выберите наладчика" 
          class="w-full"
          @focus="loadPersonnel"
          filterable
        >
          <el-option
            v-for="person in personnel"
            :key="person.id"
            :label="person.fullName"
            :value="person.id"
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
          :min="1" 
          :max="1000000"
          class="w-full"
          controls-position="right"
        />
      </el-form-item>

      <el-form-item label="Плановая дата" prop="plannedDate">
        <el-date-picker
          v-model="form.plannedDate"
          type="date"
          placeholder="Выберите дату"
          format="DD.MM.YYYY"
          value-format="YYYY-MM-DD"
          :disabled-date="isPastDate"
          class="w-full"
        />
      </el-form-item>

      <el-form-item label="Примечание">
        <el-input
          v-model="form.note"
          type="textarea"
          :rows="3"
          placeholder="Дополнительная информация"
        />
      </el-form-item>

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

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  task: {
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
const personnel = ref([])
const orderOptions = ref([])
const initialOrderId = ref(null)

const form = reactive({
  immId: '',
  moldId: '',
  personnelId: '',
  planQuantity: 1000,
  plannedDate: null,
  note: '',
  orderId: null
})

const rules = {
  immId: [{ required: true, message: 'Выберите ТПА', trigger: 'change' }],
  moldId: [{ required: true, message: 'Выберите пресс-форму', trigger: 'change' }],
  planQuantity: [
    { required: true, message: 'Введите план', trigger: 'blur' },
    { type: 'number', min: 1, message: 'План должен быть больше 0', trigger: 'blur' }
  ]
}

const selectedMoldInfo = computed(() => {
  if (!form.moldId) return null
  return molds.value.find(m => m.id === form.moldId)
})

const selectedProductTypeId = computed(() => selectedMoldInfo.value?.productTypeId || null)

const resetForm = () => {
  Object.assign(form, {
    immId: '',
    moldId: '',
    personnelId: '',
    planQuantity: 1000,
    plannedDate: null,
    note: '',
    orderId: null
  })
  orderOptions.value = []
  initialOrderId.value = null
}

const loadOrdersForProductType = async (productTypeId) => {
  if (!productTypeId) {
    orderOptions.value = []
    form.orderId = null
    return
  }
  try {
    const response = await ordersApi.getList({ status: 'Active', productTypeId })
    orderOptions.value = response.data
    if (form.orderId && !orderOptions.value.some(o => o.id === form.orderId)) {
      form.orderId = null
    }
  } catch (error) {
    ElMessage.error('Ошибка загрузки заказов')
  }
}

watch(selectedProductTypeId, (productTypeId) => {
  loadOrdersForProductType(productTypeId)
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
    personnelId: task.personnelId,
    planQuantity: task.planQuantity,
    plannedDate: task.plannedDate ? task.plannedDate.slice(0, 10) : null,
    note: task.note || '',
    orderId: task.orderId || null
  })
  initialOrderId.value = task.orderId || null
  // В режиме редактирования селект ПФ задизейблен и никогда не получает
  // @focus, поэтому molds (а значит и productTypeId для фильтра заказов)
  // нужно подгрузить явно, иначе селект «Заказ» останется пустым/disabled.
  loadMolds()
}

watch(() => props.task, (newTask) => {
  if (newTask) {
    populateForm(newTask)
  } else {
    editingTask.value = null
    resetForm()
  }
}, { immediate: true })

// Перезаполняем при каждом открытии диалога, так как task может быть
// тем же объектом (та же ссылка) — watch выше не сработает повторно.
watch(() => props.modelValue, (isVisible) => {
  if (isVisible && props.task) {
    populateForm(props.task)
  }
})

const loadImms = async () => {
  if (imms.value.length > 0) return
  try {
    const response = await immApi.getList({ isActive: true })
    imms.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}

const loadPersonnel = async () => {
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
        // orderId не входит в тело update — заказ меняется только через
        // ordersApi.setTaskOrder (единая точка привязки/смены заказа).
        const { orderId, ...updatePayload } = form
        await tasksApi.update(editingTask.value.id, updatePayload)
        if (form.orderId && form.orderId !== initialOrderId.value) {
          await ordersApi.setTaskOrder(editingTask.value.id, form.orderId)
        }
        ElMessage.success('Задание обновлено')
      } else {
        await tasksApi.create(form)
        ElMessage.success('Задание выдано')
      }

      visible.value = false
      emit('success')
    } catch (error) {
      ElMessage.error('Ошибка сохранения задания')
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