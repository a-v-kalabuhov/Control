<template>
  <el-dialog
    v-model="visible"
    :title="`Настройка эмуляции: ${imm?.name || imm?.id}`"
    width="90%"
    :close-on-click-modal="false"
    @closed="onClose"
  >
    <el-form :model="form" label-position="top" size="default">
      
      <!-- Общие настройки -->
      <el-divider content-position="left">Общие настройки</el-divider>
      <el-row :gutter="20">
        <el-col :span="12">
          <el-form-item label="Сообщений в минуту">
            <el-input-number 
              v-model="form.messagesPerMinute" 
              :min="1" 
              :max="60"
              :step="1"
            />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- Профиль работы -->
      <el-divider content-position="left">
        Профиль работы
        <el-button link type="primary" size="small" @click="addProfileStep">
          + Добавить шаг
        </el-button>
      </el-divider>
      
      <ProfileEditor v-model:steps="form.profile" :debug-mode="false" />

      <!-- Сенсоры -->
      <el-divider content-position="left">
        Сенсоры
        <el-button link type="primary" size="small" @click="loadFromTemplate" :loading="loadingTemplate">
          Загрузить из шаблона
        </el-button>
      </el-divider>
      
      <SensorEditor 
        v-model:sensors="form.sensorConfigs" 
        :template-id="imm?.templateId"
        :debug-mode="false"
      />


    </el-form>

    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button type="primary" @click="onSave" :loading="saving">
        Сохранить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import ProfileEditor from './ProfileEditor.vue'
import SensorEditor from './SensorEditor.vue'
import { useEmulator } from '../composables/useEmulator'
import { useValidation } from '../composables/useValidation'

const props = defineProps({
  modelValue: Boolean,
  imm: Object
})

const emit = defineEmits(['update:modelValue', 'saved', 'started'])

const visible = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v)
})

const { startEmulation, loadPreset, savePreset, loadTemplateSensors } = useEmulator()
const { validateEmulation } = useValidation()

const form = ref({
  messagesPerMinute: 10,
  profile: [],
  sensorConfigs: []
})

const loadingTemplate = ref(false)
const saving = ref(false)  // ← Индикатор сохранения
const isEditing = ref(false)

// Загрузка пресета при открытии диалога
watch(
  () => [props.imm?.id, visible.value],
  async ([immId, isVisible]) => {
    if (!immId || !isVisible) return
    
    try {
      const preset = await loadPreset(immId)
      if (preset) {
        form.value = {
          messagesPerMinute: preset.messagesPerMinute || 10,
          profile: preset.profile || [],
          sensorConfigs: [...(preset.sensorConfigs || [])]
        }
        isEditing.value = true
        ElMessage.info('Загружен сохранённый пресет')
      } else {
        resetForm()
        isEditing.value = false
      }
    } catch (e) {
      console.error('Error loading preset:', e)
      resetForm()
      isEditing.value = false
    }
  },
  { immediate: true }
)

const resetForm = () => {
  form.value = {
    messagesPerMinute: 10,
    profile: [],
    sensorConfigs: []
  }
}

const addProfileStep = () => {
  form.value.profile.push({
    mode: 'auto',
    durationSeconds: 60
  })
}

const loadFromTemplate = async () => {
  if (!props.imm?.templateId) {
    ElMessage.warning('У ТПА не указан шаблон')
    return
  }
  
  loadingTemplate.value = true
  try {
    const sensors = await loadTemplateSensors(props.imm.templateId)
    form.value.sensorConfigs = [...sensors]
    ElMessage.success(`Загружено ${sensors.length} сенсоров. Требуется настройка базовых значений.`)
  } finally {
    loadingTemplate.value = false
  }
}

const onSave = async () => {
  const { isValid, errors } = validateEmulation(form.value.profile, form.value.sensorConfigs)
  
  if (!isValid) {
    ElMessageBox.alert(
      errors.join('<br>'),
      'Невозможно сохранить конфигурацию',
      { 
        confirmButtonText: 'Понятно',
        type: 'warning',
        dangerouslyUseHTMLString: true
      }
    )
    return
  }

  if (!props.imm?.id) return
  
  saving.value = true
  try {
    const success = await savePreset(props.imm.id, form.value)
    if (success) {
      visible.value = false  // ← Закрываем диалог
      emit('saved')          // ← Уведомляем родителя о сохранении
      ElMessage.success('Пресет сохранён')
    }
  } catch (e) {
    ElMessage.error('Ошибка сохранения пресета')
  } finally {
    saving.value = false
  }
}

const onClose = () => {
  resetForm()
  isEditing.value = false
}
</script>

<style scoped>
.el-divider { margin: 20px 0; }
</style>