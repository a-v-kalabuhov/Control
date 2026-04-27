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
      
      <ProfileEditor v-model:steps="form.profile" />

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
      />

      <!-- Пресеты -->
      <el-divider content-position="left">Пресеты</el-divider>
      <PresetManager 
        :imm-id="imm?.id"
        :form="form"
        @load="onPresetLoad"
        @save="onPresetSave"
      />

    </el-form>

    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button type="primary" @click="onStart" :loading="starting">
        {{ isEditing ? 'Сохранить' : 'Сохранить и запустить' }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import ProfileEditor from './ProfileEditor.vue'
import SensorEditor from './SensorEditor.vue'
import PresetManager from './PresetManager.vue'
import { useEmulator } from '../composables/useEmulator'
import { useValidation } from '../composables/useValidation'

const props = defineProps({
  modelValue: Boolean,
  imm: Object
})

const emit = defineEmits(['update:modelValue', 'started'])

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
const starting = ref(false)
const isEditing = ref(false)

// Загрузка пресета при открытии
watch(
  () => [props.imm?.id, visible.value], 
  async ([immId, isVisible]) => {
    console.log('loadPreset 1', immId, visible.value)
    if (!immId || !isVisible) return
    console.log('loadPreset for', immId)    
    try {
      const preset = await loadPreset(immId)
      if (preset) {
        console.log('loadPreset ', preset)
        form.value = {
          messagesPerMinute: preset.messagesPerMinute || 10,
          profile: [...preset.profile] || [],
          sensorConfigs: [...preset.sensorConfigs] || []
        }
        isEditing.value = true
        ElMessage.info('Загружен сохранённый пресет')
      } else {
        console.log('Пресет не найден')
        resetForm()
        isEditing.value = false
      }
    } catch (e) {
      resetForm()
      isEditing.value = false
    }
  }, 
  { immediate: true })

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
  console.log('loadFromTemplate', props.imm)  
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

const onPresetLoad = (preset) => {
  form.value = {
    messagesPerMinute: preset.messagesPerMinute || 10,
    profile: [...preset.profile] || [],
    sensorConfigs: preset.sensorConfigs || []
  }
  isEditing.value = true
  ElMessage.success('Пресет загружен')
}

const onPresetSave = async () => {
  if (!props.imm?.id) return
  await savePreset(props.imm.id, form.value)
}

const onStart = async () => {
  // Валидация
  const { isValid, errors } = validateEmulation(form.value.profile, form.value.sensorConfigs)
  
  if (!isValid) {
    ElMessageBox.alert(
      errors.join('<br>'),
      'Невозможно запустить эмуляцию',
      { 
        confirmButtonText: 'Понятно',
        type: 'warning',
        dangerouslyUseHTMLString: true
      }
    )
    return
  }

  if (!props.imm?.id) return
  
  starting.value = true
  try {
    const success = await startEmulation(props.imm.id, form.value)
    if (success) {
      visible.value = false
      emit('started')
    }
  } finally {
    starting.value = false
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