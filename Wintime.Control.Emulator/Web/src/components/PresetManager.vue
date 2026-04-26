<template>
  <div class="preset-manager">
    <el-space wrap>
      <el-button @click="onSave" :disabled="!immId">
        <el-icon><Download /></el-icon> Сохранить пресет
      </el-button>
      <el-button @click="onLoad" :disabled="!immId">
        <el-icon><Upload /></el-icon> Загрузить пресет
      </el-button>
    </el-space>
    
    <el-alert 
      v-if="immId" 
      type="info" 
      :closable="false" 
      class="mt-2"
      show-icon
    >
      Пресет сохраняется в файл: <code>presets/{{ immId }}.json</code>
    </el-alert>
  </div>
</template>

<script setup>
import { ElMessage, ElMessageBox } from 'element-plus'
import { Download, Upload } from '@element-plus/icons-vue'
import { useEmulator } from '../composables/useEmulator'

const props = defineProps({
  immId: String,
  form: Object
})

const emit = defineEmits(['save', 'load'])

const { loadPreset, savePreset } = useEmulator()

const onSave = async () => {
  if (!props.immId) return
  
  const success = await savePreset(props.immId, props.form)
  if (success) {
    emit('save', props.form)
  }
}

const onLoad = async () => {
  if (!props.immId) return
  
  try {
    const preset = await loadPreset(props.immId)
    if (preset) {
      await ElMessageBox.confirm(
        'Загрузка пресета перезапишет текущие настройки. Продолжить?',
        'Подтверждение',
        { confirmButtonText: 'Да', cancelButtonText: 'Нет', type: 'warning' }
      )
      emit('load', preset)
    } else {
      ElMessage.info('Пресет для этого ТПА не найден')
    }
  } catch (e) {
    if (e !== 'cancel') {
      ElMessage.error('Ошибка загрузки пресета')
    }
  }
}
</script>

<style scoped>
.preset-manager { padding: 10px 0; }
.mt-2 { margin-top: 12px; }
code { 
  background: #f4f4f5; 
  padding: 2px 6px; 
  border-radius: 4px;
  font-size: 0.9em;
}
</style>