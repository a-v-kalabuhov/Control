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
          @refresh="refreshImms"
          @configure="openConfig"
          @start="startEmulation"
          @stop="stopEmulation"
        />
      </el-main>

      <!-- Диалог конфигурации -->
      <EmulationDialog 
        v-model="configDialogVisible"
        :imm="selectedImm"
        @started="onEmulationStarted"
      />
    </div>
  </el-config-provider>
</template>

<script setup>
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import ru from 'element-plus/es/locale/lang/ru'
import { usePolling } from './composables/usePolling'
import { useEmulator } from './composables/useEmulator'
import ImmList from './components/ImmList.vue'
import EmulationDialog from './components/EmulationDialog.vue'

const { data: imms, loading, refresh: refreshImms } = usePolling(
  () => import('./api/client').then(m => m.emulatorApi.getImms()),
  null,
  tre
)

const { startEmulation, stopEmulation } = useEmulator()

// Статусы эмуляций (отдельный polling)
const statuses = ref({})
const refreshStatuses = async () => {
  try {
    const res = await import('./api/client').then(m => m.emulatorApi.getInstances())
    statuses.value = Object.fromEntries(
      (res.data || []).map(s => [s.immId, s.status])
    )
  } catch (e) {
    console.error('Failed to fetch statuses:', e)
  }
}
// Polling статусов каждые 3 сек
setInterval(refreshStatuses, 10000)
refreshStatuses()

const configDialogVisible = ref(false)
const selectedImm = ref(null)

const openConfig = (imm) => {
  selectedImm.value = imm
  configDialogVisible.value = true
}

const onEmulationStarted = () => {
  refreshStatuses()
  ElMessage.success('Эмуляция запущена')
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