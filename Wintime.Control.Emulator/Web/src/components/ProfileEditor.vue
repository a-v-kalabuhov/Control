<template>
  <el-table :data="steps" style="width: 100%" size="small">
    <el-table-column label="#" width="50" type="index" />
    
    <el-table-column label="Режим" width="150">
      <template #default="{ row }">
        <el-select v-model="row.mode" placeholder="Выберите режим">
          <el-option label="Авто (auto)" value="auto" />
          <el-option label="Ручной (manual)" value="manual" />
          <el-option label="Простой (idle)" value="idle" />
        </el-select>
      </template>
    </el-table-column>
    
    <el-table-column label="Длительность (сек)" width="180">
      <template #default="{ row }">
        <el-input-number 
          v-model="row.durationSeconds" 
          :min="1" 
          :max="3600"
          :step="10"
        />
      </template>
    </el-table-column>
    
    <el-table-column label="Действия" width="100">
      <template #default="{ $index }">
        <el-button 
          link 
          type="danger" 
          :icon="Delete" 
          @click="removeStep($index)"
          title="Удалить шаг"
        />
      </template>
    </el-table-column>
  </el-table>
  
  <el-empty v-if="steps.length === 0" description="Нет шагов профиля" :image-size="60">
    <el-button type="primary" @click="$emit('add-step')">+ Добавить шаг</el-button>
  </el-empty>
</template>

<script setup>
import { computed } from 'vue'
import { Delete } from '@element-plus/icons-vue'

const props = defineProps({
  steps: Array
})

const emit = defineEmits(['update:steps', 'add-step'])

const steps = computed({
  get: () => props.steps || [],
  set: (v) => emit('update:steps', v)
})

const removeStep = (index) => {
  steps.value.splice(index, 1)
}
</script>