<template>
  <el-card class="mb-4">
    <el-form :inline="true" class="task-filters">
      <el-form-item label="Поиск">
        <el-input 
          v-model="localFilters.search" 
          placeholder="ТПА, ПФ, наладчик"
          clearable
          prefix-icon="Search"
          class="w-64"
          @keyup.enter="applyFilters"
        />
      </el-form-item>

      <el-form-item label="Статус">
        <el-select 
          v-model="localFilters.status" 
          placeholder="Все" 
          clearable
          class="w-40"
        >
          <el-option label="Черновик" value="Draft" />
          <el-option label="Выдано" value="Issued" />
          <el-option label="В работе" value="InProgress" />
          <el-option label="Выполнено" value="Completed" />
          <el-option label="Закрыто" value="Closed" />
        </el-select>
      </el-form-item>

      <el-form-item label="ТПА">
        <el-select 
          v-model="localFilters.immId" 
          placeholder="Все" 
          clearable
          class="w-48"
          @focus="loadImms"
        >
          <el-option
            v-for="imm in imms"
            :key="imm.id"
            :label="imm.name"
            :value="imm.id"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="Наладчик">
        <el-select 
          v-model="localFilters.personnelId" 
          placeholder="Все" 
          clearable
          class="w-48"
          @focus="loadPersonnel"
        >
          <el-option
            v-for="person in personnel"
            :key="person.id"
            :label="person.fullName"
            :value="person.id"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="Период">
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          range-separator="—"
          start-placeholder="Начало"
          end-placeholder="Окончание"
          value-format="YYYY-MM-DD"
          class="w-64"
        />
      </el-form-item>

      <el-form-item>
        <el-button type="primary" @click="applyFilters">Применить</el-button>
        <el-button @click="resetFilters">Сбросить</el-button>
      </el-form-item>
    </el-form>
  </el-card>
</template>

<script setup>
import { ref, reactive, watch } from 'vue'
import { immApi } from '@/api/imm'
import { personnelApi } from '@/api/personnel'

const props = defineProps({
  filters: {
    type: Object,
    required: true
  }
})

const emit = defineEmits(['update:filters', 'apply'])

const localFilters = reactive({ ...props.filters })
const imms = ref([])
const personnel = ref([])

const dateRange = ref(props.filters.dateFrom ? [props.filters.dateFrom, props.filters.dateTo] : [])

watch(dateRange, (newRange) => {
  if (newRange && newRange.length === 2) {
    localFilters.dateFrom = newRange[0]
    localFilters.dateTo = newRange[1]
  } else {
    localFilters.dateFrom = null
    localFilters.dateTo = null
  }
})

const loadImms = async () => {
  if (imms.value.length > 0) return
  try {
    const response = await immApi.getList({ isActive: true })
    imms.value = response.data
  } catch (error) {
    console.error('Ошибка загрузки ТПА:', error)
  }
}

const loadPersonnel = async () => {
  if (personnel.value.length > 0) return
  try {
    const response = await personnelApi.getList({ isActive: true, role: 'Adjuster' })
    personnel.value = response.data
  } catch (error) {
    console.error('Ошибка загрузки персонала:', error)
  }
}

const applyFilters = () => {
  emit('update:filters', { ...localFilters })
  emit('apply')
}

const resetFilters = () => {
  Object.keys(localFilters).forEach(key => {
    localFilters[key] = null
  })
  localFilters.search = ''
  dateRange.value = []
  emit('update:filters', { ...localFilters })
  emit('apply')
}
</script>

<style scoped>
.task-filters :deep(.el-form-item) {
  @apply mb-2;
}
</style>