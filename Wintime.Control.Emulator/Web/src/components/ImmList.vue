<template>
  <div class="imm-list">
    <div class="header">
      <h2>Список ТПА</h2>
      <el-button 
        type="primary" 
        @click="$emit('refresh')" 
        :loading="loading"
        :disabled="!!error"
      >
        <el-icon><Refresh /></el-icon> Обновить
      </el-button>
    </div>

    <!-- ОШИБКА: Показываем вместо пустого списка -->
    <el-alert
      v-if="error"
      :title="errorTitle"
      :type="errorType"
      :closable="false"
      show-icon
      class="error-alert"
    >
      <template #default>
        <p>{{ error.message }}</p>
        <p v-if="error.details" class="error-details">
          <small>Детали: {{ error.details }}</small>
        </p>
        <el-button 
          type="primary" 
          size="small" 
          @click="$emit('refresh')"
          style="margin-top: 10px"
        >
          <el-icon><Refresh /></el-icon> Повторить
        </el-button>
        
        <!-- Подсказка для авторизации -->
        <el-collapse v-if="error.code === 'AUTH_FAILED'" style="margin-top: 15px">
          <el-collapse-item title="Как исправить?">
            <ol>
              <li>Проверьте логин и пароль в <code>appsettings.json</code></li>
              <li>Убедитесь, что пользователь создан в основном API</li>
              <li>Проверьте, что у пользователя есть права на чтение ТПА</li>
              <li>Перезапустите эмулятор после изменений</li>
            </ol>
          </el-collapse-item>
        </el-collapse>
        
        <!-- Подсказка для недоступности API -->
        <el-collapse v-if="error.code === 'API_UNAVAILABLE' || error.code === 'API_TIMEOUT'" style="margin-top: 15px">
          <el-collapse-item title="Как исправить?">
            <ol>
              <li>Убедитесь, что основной API запущен</li>
              <li>Проверьте URL в <code>appsettings.json</code> (MainApi:BaseUrl)</li>
              <li>Проверьте сетевое подключение</li>
              <li>Посмотрите логи основного API на наличие ошибок</li>
            </ol>
          </el-collapse-item>
        </el-collapse>
      </template>
    </el-alert>

    <!-- Пустой список (только если нет ошибки) -->
    <el-empty 
      v-else-if="!loading && (!imms || imms.length === 0)" 
      description="Нет доступных ТПА" 
    >
      <el-button type="primary" @click="$emit('refresh')">
        <el-icon><Refresh /></el-icon> Обновить
      </el-button>
    </el-empty>

    <!-- Список карточек -->
    <el-row :gutter="20" v-else>
      <el-col :xs="24" :sm="12" :lg="8" v-for="imm in imms" :key="imm.id">
        <ImmCard 
          :imm="imm"
          :status="statuses[imm.id]"
          @configure="$emit('configure', imm)"
          @start="$emit('start', imm)"
          @stop="$emit('stop', imm)"
        />
      </el-col>
    </el-row>

    <!-- Загрузка -->
    <el-skeleton :rows="5" animated v-if="loading && !error" />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import ImmCard from './ImmCard.vue'

const props = defineProps({
  imms: Array,
  statuses: Object,
  loading: Boolean,
  error: Object  // Получаем ошибку от родителя
})

defineEmits(['refresh', 'configure', 'start', 'stop'])

const errorTitle = computed(() => {
  const titles = {
    'AUTH_FAILED': '❌ Ошибка авторизации',
    'ACCESS_DENIED': '❌ Нет прав доступа',
    'API_UNAVAILABLE': '❌ Основной API недоступен',
    'API_TIMEOUT': '⏱️ Превышено время ожидания',
    'NETWORK_ERROR': '❌ Ошибка сети',
    'INTERNAL_ERROR': '❌ Внутренняя ошибка'
  }
  return titles[props.error?.code] || '❌ Ошибка загрузки'
})

const errorType = computed(() => {
  const types = {
    'AUTH_FAILED': 'error',
    'ACCESS_DENIED': 'error',
    'API_UNAVAILABLE': 'error',
    'API_TIMEOUT': 'warning',
    'NETWORK_ERROR': 'error',
    'INTERNAL_ERROR': 'error'
  }
  return types[props.error?.code] || 'error'
})
</script>

<style scoped>
.imm-list { padding: 20px; }
.header { 
  display: flex; 
  justify-content: space-between; 
  align-items: center; 
  margin-bottom: 20px;
}
.error-alert {
  margin-bottom: 20px;
}
.error-details {
  color: #909399;
  margin-top: 8px;
}
code {
  background: #f4f4f5;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.9em;
}
</style>