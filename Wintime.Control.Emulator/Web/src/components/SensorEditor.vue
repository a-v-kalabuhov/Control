<template>
  <el-table :data="sensors" style="width: 100%" size="small" max-height="400">
    <el-table-column label="Сенсор" min-width="150">
      <template #default="{ row }">
        <strong>{{ row.name }}</strong>
        <el-tag size="small" :type="sensorTypeTag(row.type)" class="ml-2">
          {{ sensorTypeLabel(row.type) }}
        </el-tag>
      </template>
    </el-table-column>
    
    <!-- Float сенсоры -->
    <template v-if="row.type === 'float'">
      <el-table-column label="Авто" width="100">
        <template #default="{ row }">
          <el-input-number v-model="row.baseValueAuto" :step="0.1" :precision="1" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="Ручной" width="100">
        <template #default="{ row }">
          <el-input-number v-model="row.baseValueManual" :step="0.1" :precision="1" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="Простой" width="100">
        <template #default="{ row }">
          <el-input-number v-model="row.baseValueIdle" :step="0.1" :precision="1" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="Разброс %" width="90">
        <template #default="{ row }">
          <el-input-number v-model="row.variancePercent" :min="0" :max="100" size="small" />
        </template>
      </el-table-column>
    </template>
    
    <!-- Boolean сенсоры -->
    <template v-if="row.type === 'boolean'">
      <el-table-column label="Авто" width="80">
        <template #default="{ row }">
          <el-switch v-model="row.valueAuto" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="Ручной" width="80">
        <template #default="{ row }">
          <el-switch v-model="row.valueManual" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="Простой" width="80">
        <template #default="{ row }">
          <el-switch v-model="row.valueIdle" size="small" />
        </template>
      </el-table-column>
    </template>
    
    <!-- String сенсоры -->
    <template v-if="row.type === 'string'">
      <el-table-column label="Авто" width="120">
        <template #default="{ row }">
          <el-input v-model="row.stringValueAuto" size="small" placeholder="значение" />
        </template>
      </el-table-column>
      <el-table-column label="Ручной" width="120">
        <template #default="{ row }">
          <el-input v-model="row.stringValueManual" size="small" placeholder="значение" />
        </template>
      </el-table-column>
      <el-table-column label="Простой" width="120">
        <template #default="{ row }">
          <el-input v-model="row.stringValueIdle" size="small" placeholder="значение" />
        </template>
      </el-table-column>
    </template>
    
    <el-table-column label="Статус" width="100" fixed="right">
      <template #default="{ row }">
        <el-tag 
          :type="isSensorConfigured(row) ? 'success' : 'warning'" 
          size="small"
          :effect="isSensorConfigured(row) ? 'light' : 'plain'"
        >
          {{ isSensorConfigured(row) ? '✓ Готов' : '⚠ Настроить' }}
        </el-tag>
      </template>
    </el-table-column>
  </el-table>
  
  <el-empty v-if="sensors.length === 0" description="Нет сенсоров" :image-size="60" />
</template>

<script setup>
import { computed } from 'vue'
import { useValidation } from '../composables/useValidation'

const props = defineProps({
  sensors: Array
})

const emit = defineEmits(['update:sensors'])

const { isSensorConfigured } = useValidation()

const sensors = computed({
  get: () => props.sensors || [],
  set: (v) => emit('update:sensors', v)
})

const sensorTypeLabel = (type) => ({
  'float': 'Число',
  'boolean': 'Логика',
  'string': 'Строка',
  'cycleCounter': 'Счётчик'
}[type] || type)

const sensorTypeTag = (type) => ({
  'float': '',
  'boolean': 'info',
  'string': 'warning',
  'cycleCounter': 'success'
}[type] || 'info')
</script>

<style scoped>
.ml-2 { margin-left: 8px; }
</style>