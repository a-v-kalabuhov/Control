<template>
  <div class="max-w-2xl">
    <h2 class="text-xl font-semibold text-gray-800 mb-6">Режим обслуживания</h2>

    <!-- Текущий статус -->
    <el-card class="mb-6">
      <div class="flex items-center justify-between">
        <div>
          <div class="text-base font-medium">Статус</div>
          <div class="text-sm text-gray-500 mt-1">
            В режиме обслуживания обычные пользователи получают ошибку 503.
          </div>
        </div>
        <el-tag :type="maintenanceActive ? 'danger' : 'success'" size="large">
          {{ maintenanceActive ? 'Обслуживание' : 'Активен' }}
        </el-tag>
      </div>

      <div class="flex gap-3 mt-4">
        <el-button
          v-if="!maintenanceActive"
          type="warning"
          :loading="actionLoading"
          @click="enterMaintenance"
        >
          Войти в режим обслуживания
        </el-button>
        <el-button
          v-else
          type="success"
          :loading="actionLoading"
          @click="exitMaintenance"
        >
          Выйти из режима обслуживания
        </el-button>
      </div>
    </el-card>

    <!-- Миграции БД -->
    <el-card class="mb-6">
      <div class="font-medium mb-1">Миграции базы данных</div>
      <div class="text-sm text-gray-500 mb-4">
        Применяет все pending EF-миграции. Рекомендуется делать бэкап перед применением.
      </div>
      <el-button type="primary" :loading="migrateLoading" @click="runMigrations">
        Применить миграции
      </el-button>
      <div v-if="migrateResult" class="mt-3 text-sm text-green-600">
        Применено: {{ migrateResult.appliedMigrations?.join(', ') || 'нет изменений' }}
      </div>
    </el-card>

    <!-- Перезапуск -->
    <el-card>
      <div class="font-medium mb-1">Перезапуск приложения</div>
      <div class="text-sm text-gray-500 mb-4">
        Перезапускает процесс сервера. Займёт 5–10 секунд. Все подключения будут прерваны.
      </div>
      <el-popconfirm
        title="Перезапустить приложение?"
        confirm-button-text="Да, перезапустить"
        cancel-button-text="Отмена"
        @confirm="restartApp"
      >
        <template #reference>
          <el-button type="danger" :loading="restartLoading">Перезапустить</el-button>
        </template>
      </el-popconfirm>
    </el-card>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { modulesApi } from '@/api/modules'

const maintenanceActive = ref(false)
const actionLoading = ref(false)
const migrateLoading = ref(false)
const restartLoading = ref(false)
const migrateResult = ref(null)

onMounted(async () => {
  try {
    const res = await modulesApi.getMaintenanceStatus()
    maintenanceActive.value = res.data.isActive
  } catch {
    ElMessage.error('Не удалось получить статус обслуживания')
  }
})

async function enterMaintenance() {
  actionLoading.value = true
  try {
    await modulesApi.enterMaintenance()
    maintenanceActive.value = true
    ElMessage.success('Режим обслуживания включён')
  } catch {
    ElMessage.error('Ошибка')
  } finally {
    actionLoading.value = false
  }
}

async function exitMaintenance() {
  actionLoading.value = true
  try {
    await modulesApi.exitMaintenance()
    maintenanceActive.value = false
    ElMessage.success('Режим обслуживания выключен')
  } catch {
    ElMessage.error('Ошибка')
  } finally {
    actionLoading.value = false
  }
}

async function runMigrations() {
  migrateLoading.value = true
  migrateResult.value = null
  try {
    const res = await modulesApi.applyMigrations()
    migrateResult.value = res.data
    ElMessage.success('Миграции применены')
  } catch {
    ElMessage.error('Ошибка при применении миграций')
  } finally {
    migrateLoading.value = false
  }
}

async function restartApp() {
  restartLoading.value = true
  try {
    await modulesApi.restartApp()
    ElMessage.info('Сервер перезапускается...')
  } catch {
    // 503/connection error ожидаем — сервер уже уходит
    ElMessage.info('Сервер перезапускается...')
  } finally {
    restartLoading.value = false
  }
}
</script>
