<template>
  <el-config-provider :locale="ru">
    <div class="app">
      <el-header class="app-header">
        <h1>🏭 Эмулятор ТПА</h1>
      </el-header>
      
      <el-main>
        <ImmList 
          :imms="imms" 
          :statuses="statuses"
          :loading="loading"
          :error="immsError"
          @refresh="refreshImms"
          @configure="openConfig"
          @start="startEmulationWithCheck"
          @stop="stopEmulation"
        />
      </el-main>

      <!-- Диалог конфигурации -->
      <EmulationDialog 
        v-model="configDialogVisible"
        :imm="selectedImm"
        @started="onEmulationSaved"
      />
    </div>
  </el-config-provider>
</template>

<script setup>
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import ru from 'element-plus/es/locale/lang/ru'
import { usePolling } from './composables/usePolling'
import { useEmulator } from './composables/useEmulator'
import { emulatorApi } from './api/client'
import ImmList from './components/ImmList.vue'
import EmulationDialog from './components/EmulationDialog.vue'

// ← Получаем error из usePolling
const { data: imms, loading, error: immsError, refresh: refreshImms } = usePolling(
  () => emulatorApi.getImms(),
  null,
  true
)

const { startEmulation, stopEmulation, loadPreset } = useEmulator()

// Статусы эмуляций (отдельный polling)
const statuses = ref({})
const refreshStatuses = async () => {
  try {
    const res = emulatorApi.getInstances()
    statuses.value = Object.fromEntries(
      (res.data || []).map(s => [s.immId, s.status])
    )
  } catch (e) {
    console.error('Failed to fetch statuses:', e)
  }
}
// Polling статусов каждые 3 сек
const pollingInterval = parseInt(import.meta.env.VITE_API_TIMEOUT) || 3000
setInterval(refreshStatuses, pollingInterval)
refreshStatuses()

const configDialogVisible = ref(false)
const selectedImm = ref(null)

const openConfig = (imm) => {
  console.log('openConfig', imm)
  if (!imm) {
    ElMessage.error('Не выбран ТПА')
    return
  }
  selectedImm.value = imm
  configDialogVisible.value = true
}

const onEmulationStarted = () => {
  refreshStatuses()
  ElMessage.success('Эмуляция запущена')
}

const onEmulationSaved = () => {
  // Просто обновляем список (визуально ничего не меняется, но можно показать уведомление)
  ElMessage.success('Конфигурация сохранена')
}

const startEmulationWithCheck = async (imm) => {
  // ← Проверяем наличие пресета перед запуском
  try {
    const preset = await loadPreset(imm.id)
    if (!preset || !preset.profile?.length || !preset.sensorConfigs?.length) {
      ElMessageBox.alert(
        'Сначала настройте эмуляцию (кнопка "Настроить") и сохраните конфигурацию.',
        'Нет сохранённой конфигурации',
        { 
          confirmButtonText: 'Понятно',
          type: 'warning'
        }
      )
      return
    }
    
    // Пресет есть — запускаем
    const success = await startEmulation(imm.id, preset)
    if (success) {
      refreshStatuses()
    }
  } catch (e) {
    ElMessage.error('Ошибка при запуске эмуляции')
    console.error(e)
  }
}
</script>

<style>
/* Глобальные стили */
* { box-sizing: border-box; margin: 0; padding: 0; }

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
  background: #f5f7fa;
  color: #303133;
}

.app {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.app-header {
  background: #409eff;
  color: white;
  padding: 0 20px;
  display: flex;
  align-items: center;
  box-shadow: 0 2px 12px 0 rgba(0,0,0,0.1);
}

.app-header h1 {
  font-size: 1.4rem;
  font-weight: 500;
}

.el-main {
  padding: 20px;
}
</style>