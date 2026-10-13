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
          placeholder="Активные"
          clearable
          class="w-52"
        >
          <el-option-group label="Активные">
            <el-option label="Планируется" value="Planned" />
            <el-option label="Выдано" value="Issued" />
            <el-option label="Наладка" value="Setup" />
            <el-option label="В работе" value="InProgress" />
          </el-option-group>
          <el-option-group label="Архив">
            <el-option label="Выполнено" value="Completed" />
            <el-option label="Закрыто" value="Closed" />
          </el-option-group>
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

      <el-form-item label="Выдано">
        <el-date-picker
          v-model="issuedRange"
          type="daterange"
          range-separator="—"
          start-placeholder="Начало"
          end-placeholder="Окончание"
          value-format="YYYY-MM-DD"
          class="w-64"
        />
      </el-form-item>

      <el-form-item label="Плановая дата">
        <el-date-picker
          v-model="plannedRange"
          type="daterange"
          range-separator="—"
          start-placeholder="Начало"
          end-placeholder="Окончание"
          value-format="YYYY-MM-DD"
          class="w-64"
        />
      </el-form-item>

      <el-form-item label="В работе">
        <el-date-picker
          v-model="activeRange"
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

// Три независимых диапазона (ARCH-03): выдача, плановая дата, работа на ТПА; объединяются через И.
function rangeRef(fromKey, toKey) {
  const range = ref(props.filters[fromKey] ? [props.filters[fromKey], props.filters[toKey]] : [])
  watch(range, (r) => {
    const set = r && r.length === 2
    localFilters[fromKey] = set ? r[0] : null
    localFilters[toKey] = set ? r[1] : null
  })
  return range
}

const issuedRange = rangeRef('issuedFrom', 'issuedTo')
const plannedRange = rangeRef('plannedFrom', 'plannedTo')
const activeRange = rangeRef('activeFrom', 'activeTo')

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
  issuedRange.value = []
  plannedRange.value = []
  activeRange.value = []
  emit('update:filters', { ...localFilters })
  emit('apply')
}
</script>

<style scoped>
.task-filters :deep(.el-form-item) {
  @apply mb-2;
}
</style>