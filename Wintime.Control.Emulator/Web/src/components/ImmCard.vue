<template>
  <el-card class="imm-card" :class="{ 'running': isRunning }">
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
      <div class="card-info">
        <p><small>ID ТПА: {{ imm.id }}</small></p>
        <p><small>Инв. номер: {{ imm.inventoryNumber || '—' }}</small></p>
        <p><small>Модель: {{ imm.model || '—' }}</small></p>
        <div class="message-log">
          <p v-for="(msg, i) in recentMessages" :key="i" class="message-entry">
            <small>{{ formatTime(msg.timestamp) }} Сообщение: mode="{{ msg.mode }}"</small>
          </p>
          <p v-for="i in emptyRows" :key="'empty-' + i" class="message-entry message-empty">
            <small>&nbsp;</small>
          </p>
        </div>
      </div>

      <div class="actions">
        <el-button
          size="small"
          :type="isRunning ? '' : 'warning'"
          :disabled="isRunning"
          @click="$emit('configure', imm)"
        >
          <el-icon><Setting /></el-icon> Настроить
        </el-button>
        <el-button
          size="small"
          type="primary"
          :disabled="isRunning"
          @click="$emit('start', imm)"
        >
          <el-icon><VideoPlay /></el-icon> Запустить
        </el-button>
        <el-button
          size="small"
          type="danger"
          :disabled="!isRunning"
          @click="$emit('stop', imm)"
        >
          <el-icon><VideoPause /></el-icon> Остановить
        </el-button>
      </div>
    </div>
  </el-card>
</template>

<script setup>
import { computed } from 'vue'
import { Monitor, Setting, VideoPlay, VideoPause } from '@element-plus/icons-vue'

const props = defineProps({
  imm: Object,
  status: Object
})

defineEmits(['configure', 'start', 'stop'])

const isRunning = computed(() => props.status?.status === 'Running')

const recentMessages = computed(() => props.status?.recentMessages ?? [])
const emptyRows = computed(() => Math.max(0, 5 - recentMessages.value.length))

const formatTime = (ts) => {
  const d = new Date(ts)
  return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

const statusText = computed(() => {
  if (!props.status) return 'Не запущен'
  const modeMap = {
    'idle': 'Режим работы: Простой',
    'manual': 'Режим работы: Наладка',
    'auto': 'Режим работы: Авто',
    'alarm': 'Режим работы: Авария'
  }
  return modeMap[props.status.mode] ?? 'Работает'
})

const statusTagType = computed(() => {
  if (!props.status) return 'info'
  const modeMap = {
    'idle': 'warning',
    'manual': 'primary',
    'auto': 'success',
    'alarm': 'error'
  }
  return modeMap[props.status.mode] ?? 'success'
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
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  color: #606266;
  font-size: 14px;
}

.card-info {
  flex: 1;
}

.message-log {
  margin-top: 8px;
}

.message-entry {
  color: #909399;
  line-height: 1.6;
  white-space: nowrap;
}

.message-empty {
  visibility: hidden;
}

.actions {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-left: 12px;
  flex-shrink: 0;
  width: 110px;
}

.actions .el-button {
  width: 100%;
  margin: 0;
}
</style>
