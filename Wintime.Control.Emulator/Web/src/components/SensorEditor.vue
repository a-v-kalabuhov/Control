<template>
  <div class="sensor-editor">
    <!-- Отладочная информация (удалите после проверки) -->
    <el-alert 
      v-if="debugMode" 
      type="info" 
      :closable="false" 
      class="mb-2"
    >
      <small>
        Sensors count: {{ sensors?.length || 0 }}<br>
        First sensor: {{ sensors?.[0] ? JSON.stringify(sensors[0]) : 'none' }}
      </small>
    </el-alert>

    <!-- Таблица сенсоров -->
    <el-table 
      :data="sensors" 
      style="width: 100%" 
      size="small" 
      max-height="400"
      empty-text="Нет сенсоров для отображения"
    >
      <!-- Имя и тип сенсора -->
      <el-table-column label="Сенсор" min-width="150" fixed="left">
        <template #default="{ row }">
          <div class="sensor-name">
            <strong>{{ row.name }}</strong>
            <el-tag 
              size="small" 
              :type="getSensorTypeTag(row.type)"
              class="ml-2"
            >
              {{ getSensorTypeLabel(row.type) }}
            </el-tag>
          </div>
        </template>
      </el-table-column>

      <!-- Float сенсоры: базовые значения + разброс -->
      <template v-if="hasFloatSensors">
        <el-table-column label="Авто" width="100">
          <template #default="{ row }">
            <el-input-number 
              v-if="row.type === 'float'"
              v-model="row.baseValueAuto" 
              :step="0.1" 
              :precision="1" 
              size="small"
              :min="-999999"
              :max="999999"
              placeholder="0"
            />
            <el-switch 
              v-else-if="row.type === 'boolean'"
              v-model="row.valueAuto" 
              size="small" 
              active-text="Вкл"
            />
            <el-input 
              v-else-if="row.type === 'string'"
              v-model="row.stringValueAuto" 
              size="small" 
              placeholder="значение"
            />
          </template>
        </el-table-column>

        <el-table-column label="Ручной" width="100">
          <template #default="{ row }">
            <el-input-number 
              v-if="row.type === 'float'"
              v-model="row.baseValueManual" 
              :step="0.1" 
              :precision="1" 
              size="small"
              placeholder="0"
            />
            <el-switch 
              v-else-if="row.type === 'boolean'"
              v-model="row.valueManual" 
              size="small"
            />
            <el-input 
              v-else-if="row.type === 'string'"
              v-model="row.stringValueManual" 
              size="small" 
              placeholder="значение"
            />
          </template>
        </el-table-column>

        <el-table-column label="Простой" width="100">
          <template #default="{ row }">
            <el-input-number 
              v-if="row.type === 'float'"
              v-model="row.baseValueIdle" 
              :step="0.1" 
              :precision="1" 
              size="small"
              placeholder="0"
            />
            <el-switch 
              v-else-if="row.type === 'boolean'"
              v-model="row.valueIdle" 
              size="small"
            />
            <el-input 
              v-else-if="row.type === 'string'"
              v-model="row.stringValueIdle" 
              size="small" 
              placeholder="значение"
            />
          </template>
        </el-table-column>

        <el-table-column label="Разброс %" width="90" v-if="hasFloatSensors">
          <template #default="{ row }">
            <el-input-number 
              v-if="row.type === 'float'"
              v-model="row.variancePercent" 
              :min="0" 
              :max="100" 
              size="small"
            >
              <template #suffix>%</template>
            </el-input-number>
            <span v-else class="text-gray">—</span>
          </template>
        </el-table-column>
      </template>

      <!-- Статус настройки -->
      <el-table-column label="Статус" width="110" fixed="right">
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

    <!-- Пустое состояние -->
    <el-empty 
      v-if="!sensors || sensors.length === 0" 
      description="Сенсоры не загружены. Нажмите «Загрузить из шаблона»" 
      :image-size="60"
      class="py-4"
    />
  </div>
</template>

<script setup>
import { computed, watch } from 'vue'
import { useValidation } from '../composables/useValidation'

const props = defineProps({
  sensors: {
    type: Array,
    default: () => []
  },
  templateId: String,
  debugMode: {  // ← Для отладки: покажет сырые данные
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:sensors'])

const { isSensorConfigured } = useValidation()

// ← Критически важно: v-model:sensors через computed
const sensors = computed({
  get: () => props.sensors || [],
  set: (value) => emit('update:sensors', value)
})

// Отладочный лог при изменении сенсоров
watch(() => props.sensors, (newVal) => {
  if (props.debugMode) {
    console.log('🔍 SensorEditor: sensors updated', newVal?.length, newVal)
  }
}, { deep: true })

// Проверка, есть ли хоть один float-сенсор (для отображения колонки "Разброс")
const hasFloatSensors = computed(() => 
  sensors.value?.some(s => s.type === 'float')
)

const getSensorTypeLabel = (type) => ({
  'float': 'Число',
  'boolean': 'Логика',
  'string': 'Строка',
  'cycleCounter': 'Счётчик'
}[type] || type)

const getSensorTypeTag = (type) => ({
  'float': '',
  'boolean': 'info',
  'string': 'warning',
  'cycleCounter': 'success'
}[type] || 'info')
</script>

<style scoped>
.sensor-editor { 
  width: 100%;
}
.sensor-name {
  display: flex;
  align-items: center;
  gap: 8px;
}
.ml-2 { margin-left: 8px; }
.mb-2 { margin-bottom: 12px; }
.py-4 { padding: 16px 0; }
.text-gray { color: #909399; }
</style>