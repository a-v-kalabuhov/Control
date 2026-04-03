<template>
  <div>
    <div class="mb-6">
      <h2 class="text-2xl font-bold text-gray-800">Настройки системы</h2>
      <p class="text-gray-600 mt-1">Конфигурация подключения к MQTT-брокеру и базе данных</p>
    </div>

    <el-card class="mb-6">
      <template #header>
        <div class="flex items-center justify-between">
          <span class="font-semibold">MQTT-брокер</span>
          <el-button type="primary" size="small" @click="testMqttConnection" :loading="testing">
            Проверить подключение
          </el-button>
        </div>
      </template>

      <el-form :model="settings" label-width="200px" label-position="left">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="URL брокера">
              <el-input v-model="settings.mqttBrokerUrl" placeholder="tcp://localhost" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Порт">
              <el-input-number v-model="settings.mqttPort" :min="1" :max="65535" class="w-full" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Логин">
              <el-input v-model="settings.mqttUsername" placeholder="Опционально" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="Пароль">
              <el-input v-model="settings.mqttPassword" type="password" placeholder="Опционально" show-password />
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>
    </el-card>

    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">База данных</span>
      </template>

      <el-form :model="settings" label-width="200px" label-position="left">
        <el-form-item label="Connection String">
          <el-input 
            v-model="settings.databaseConnectionString" 
            type="textarea" 
            :rows="3"
            placeholder="Host=localhost;Port=5432;Database=control_db;Username=postgres;Password=***"
          />
        </el-form-item>
      </el-form>
    </el-card>

    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Общие настройки</span>
      </template>

      <el-row :gutter="20">
        <el-col :span="12">
          <el-form-item label="Таймаут сессии (мин)">
            <el-input-number v-model="settings.sessionTimeoutMinutes" :min="5" :max="1440" class="w-full" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="Интервал телеметрии (сек)">
            <el-input-number v-model="settings.telemetryIntervalSeconds" :min="1" :max="60" class="w-full" />
          </el-form-item>
        </el-col>
      </el-row>
    </el-card>

    <div class="flex justify-end gap-4">
      <el-button @click="loadSettings">Отмена</el-button>
      <el-button type="primary" @click="saveSettings" :loading="saving">Сохранить</el-button>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { adminApi } from '@/api/admin'

const saving = ref(false)
const testing = ref(false)

const settings = reactive({
  mqttBrokerUrl: '',
  mqttPort: 1883,
  mqttUsername: '',
  mqttPassword: '',
  databaseConnectionString: '',
  sessionTimeoutMinutes: 60,
  telemetryIntervalSeconds: 5
})

onMounted(async () => {
  await loadSettings()
})

const loadSettings = async () => {
  try {
    const response = await adminApi.getSettings()
    Object.assign(settings, response.data)
  } catch (error) {
    ElMessage.error('Ошибка загрузки настроек')
  }
}

const saveSettings = async () => {
  saving.value = true
  try {
    await adminApi.updateSettings(settings)
    ElMessage.success('Настройки сохранены')
  } catch (error) {
    ElMessage.error('Ошибка сохранения настроек')
  } finally {
    saving.value = false
  }
}

const testMqttConnection = async () => {
  testing.value = true
  try {
    await adminApi.testMqttConnection({
      brokerUrl: settings.mqttBrokerUrl,
      port: settings.mqttPort,
      username: settings.mqttUsername,
      password: settings.mqttPassword
    })
    ElMessage.success('Подключение к MQTT-брокеру успешно!')
  } catch (error) {
    ElMessage.error('Ошибка подключения к MQTT-брокеру: ' + (error.response?.data?.message || error.message))
  } finally {
    testing.value = false
  }
}
</script>