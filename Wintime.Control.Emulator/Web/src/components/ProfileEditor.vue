<template>
  <div class="profile-editor">
    <!-- Отладочная информация (удалите после проверки) -->
    <el-alert 
      v-if="debugMode" 
      type="info" 
      :closable="false" 
      class="mb-2"
    >
      <small>
        Steps count: {{ steps?.length || 0 }}<br>
        First step: {{ steps?.[0] ? JSON.stringify(steps[0]) : 'none' }}
      </small>
    </el-alert>

    <!-- Таблица шагов профиля -->
    <el-table 
      :data="steps" 
      style="width: 100%" 
      size="small"
      empty-text="Нет шагов профиля"
    >
      <el-table-column label="#" width="50" type="index" />
      
      <!-- Режим работы -->
      <el-table-column label="Режим" width="200">
        <template #default="{ row, $index }">
          <el-select 
            v-model="row.mode" 
            placeholder="Выберите режим"
            size="small"
            style="width: 100%"
          >
            <el-option label="Авто (auto)" value="auto" />
            <el-option label="Ручной (manual)" value="manual" />
            <el-option label="Простой (idle)" value="idle" />
          </el-select>
        </template>
      </el-table-column>
      
      <!-- Длительность -->
      <el-table-column label="Длительность (сек)" width="180">
        <template #default="{ row }">
          <el-input-number 
            v-model="row.durationSeconds" 
            :min="1" 
            :max="3600"
            :step="10"
            size="small"
            style="width: 100%"
          />
        </template>
      </el-table-column>
      
      <!-- Действия -->
      <el-table-column label="Действия" width="100" fixed="right">
        <template #default="{ $index }">
          <el-button 
            link 
            type="danger" 
            :icon="Delete" 
            @click="removeStep($index)"
            title="Удалить шаг"
            size="small"
          />
        </template>
      </el-table-column>
    </el-table>
    
    <!-- Кнопка добавления шага -->
    <div class="add-step-container">
      <el-button 
        type="primary" 
        size="small" 
        @click="addStep"
        :icon="Plus"
      >
        + Добавить шаг
      </el-button>
    </div>
    
    <!-- Пустое состояние -->
    <el-empty 
      v-if="!steps || steps.length === 0" 
      description="Нет шагов профиля. Добавьте первый шаг." 
      :image-size="60"
      class="py-4"
    >
      <el-button type="primary" size="small" @click="addStep">
        <el-icon><Plus /></el-icon> Добавить шаг
      </el-button>
    </el-empty>
  </div>
</template>

<script setup>
import { computed, watch } from 'vue'
import { Delete, Plus } from '@element-plus/icons-vue'

const props = defineProps({
  steps: {
    type: Array,
    default: () => []
  },
  debugMode: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:steps'])

// ← Критически важно: v-model:steps через computed
const steps = computed({
  get: () => props.steps || [],
  set: (value) => {
    console.log('🔍 ProfileEditor: steps updated', value?.length, value)
    emit('update:steps', value)
  }
})

// Отладочный лог при изменении шагов
watch(() => props.steps, (newVal) => {
  if (props.debugMode) {
    console.log('🔍 ProfileEditor: props.steps changed', newVal?.length, newVal)
  }
}, { deep: true })

// ← Функция добавления шага (вызывается из родителя И из этого компонента)
const addStep = () => {
  const newStep = {
    mode: 'auto',
    durationSeconds: 60
  }
  
  console.log('🔍 Adding new step:', newStep)
  
  // ← Важно: создаём НОВЫЙ массив, а не мутируем старый
  steps.value = [...steps.value, newStep]
  
  console.log('🔍 Steps after add:', steps.value?.length)
}

// Функция удаления шага
const removeStep = (index) => {
  console.log('🔍 Removing step at index:', index)
  
  // ← Важно: создаём НОВЫЙ массив без удаляемого элемента
  steps.value = steps.value.filter((_, i) => i !== index)
  
  console.log('🔍 Steps after remove:', steps.value?.length)
}

// ← Экспортируем функции для использования из родителя (если нужно)
defineExpose({
  addStep,
  removeStep
})
</script>

<style scoped>
.profile-editor { 
  width: 100%;
}
.add-step-container {
  margin-top: 16px;
  display: flex;
  justify-content: flex-start;
}
.mb-2 { margin-bottom: 12px; }
.py-4 { padding: 16px 0; }
</style>