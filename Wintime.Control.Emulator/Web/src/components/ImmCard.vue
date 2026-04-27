<template>
  <el-card class="imm-card" :class="{ 'running': status === 'Running' }">
    <template #header>
      <div class="card-header">
        <span>
          <el-icon><Monitor /></el-icon>
          {{ imm.name || imm.id }}
        </span>
        <el-tag :type="statusTagType" size="small">
          {{ statusText }}
        </el-tag>
      </div>
    </template>

    <div class="card-body">
      <p><small>Инв. номер: {{ imm.inventoryNumber || '—' }}</small></p>
      <p><small>Модель: {{ imm.model || '—' }}</small></p>
      
      <div class="actions" v-if="status !== 'Running'">
        <el-button size="small" @click="$emit('configure', imm)">
          <el-icon><Setting /></el-icon> Настроить
        </el-button>
        <el-button 
          type="primary" 
          size="small" 
          @click="$emit('start', imm)"
          :disabled="!status || status === 'Running'"
        >
          <el-icon><VideoPlay /></el-icon> Запустить
        </el-button>
      </div>
      
      <el-button 
        v-else 
        type="danger" 
        size="small" 
        @click="$emit('stop', imm)"
      >
        <el-icon><VideoPause /></el-icon> Остановить
      </el-button>
    </div>
  </el-card>
</template>

<script setup>
import { computed } from 'vue'
import { Monitor, Setting, VideoPlay, VideoPause } from '@element-plus/icons-vue'

const props = defineProps({
  imm: Object,
  status: String
})

defineEmits(['configure', 'start', 'stop'])

const statusText = computed(() => {
  const map = {
    'Running': 'Работает',
    'Stopped': 'Остановлен',
    'Error': 'Ошибка',
    null: 'Неизвестно'
  }
  return map[props.status] || 'Неизвестно'
})

const statusTagType = computed(() => {
  const map = {
    'Running': 'success',
    'Stopped': 'info',
    'Error': 'danger'
  }
  return map[props.status] || 'info'
})
</script>

<style scoped>
.imm-card { margin-bottom: 20px; }
.imm-card.running { border-left: 4px solid #67c23a; }
.card-header { 
  display: flex; 
  justify-content: space-between; 
  align-items: center;
  font-weight: 500;
}
.card-body { 
  color: #606266; 
  font-size: 14px;
}
.actions { 
  display: flex; 
  gap: 10px; 
  margin-top: 15px;
}
</style>