<template>
  <div>
    <div class="flex items-center justify-between mb-6">
      <h2 class="text-xl font-semibold text-gray-800">Модули платформы</h2>
      <el-button :loading="loading" @click="reload">Обновить</el-button>
    </div>

    <el-table :data="modulesStore.modules" v-loading="loading" stripe>
      <el-table-column prop="key" label="Ключ" width="120" />

      <el-table-column label="Модуль">
        <template #default="{ row }">
          <div class="font-medium">{{ row.displayName || row.key }}</div>
          <div class="text-xs text-gray-400">{{ row.moduleVersion || row.installedVersion || '—' }}</div>
        </template>
      </el-table-column>

      <el-table-column label="Статус" width="160">
        <template #default="{ row }">
          <el-tag v-if="row.isLoaded && row.isEnabled" type="success">Активен</el-tag>
          <el-tag v-else-if="row.isEnabled && !row.isLoaded" type="warning">Ожидает перезапуска</el-tag>
          <el-tag v-else type="info">Отключён</el-tag>
        </template>
      </el-table-column>

      <el-table-column label="Загружен" width="110" align="center">
        <template #default="{ row }">
          <el-icon v-if="row.isLoaded" color="#22c55e"><CircleCheck /></el-icon>
          <el-icon v-else color="#9ca3af"><CircleClose /></el-icon>
        </template>
      </el-table-column>

      <el-table-column label="Включён" width="110" align="center">
        <template #default="{ row }">
          <el-switch
            :model-value="row.isEnabled"
            @change="(val) => toggleModule(row, val)"
          />
        </template>
      </el-table-column>

      <el-table-column label="Включён" width="180">
        <template #default="{ row }">
          {{ row.enabledAt ? dayjs(row.enabledAt).format('DD.MM.YYYY HH:mm') : '—' }}
        </template>
      </el-table-column>
    </el-table>

    <el-alert
      v-if="requiresRestart"
      class="mt-4"
      type="warning"
      title="Требуется перезапуск"
      description="Изменения вступят в силу после перезапуска приложения в разделе «Обслуживание»."
      show-icon
      :closable="false"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { useModulesStore } from '@/stores/modules'
import dayjs from 'dayjs'

const modulesStore = useModulesStore()
const loading = ref(false)
const requiresRestart = ref(false)

onMounted(reload)

async function reload() {
  loading.value = true
  try {
    await modulesStore.loadModules()
  } finally {
    loading.value = false
  }
}

async function toggleModule(mod, enable) {
  try {
    let result
    if (enable) {
      result = await modulesStore.enableModule(mod.key)
    } else {
      result = await modulesStore.disableModule(mod.key)
    }
    if (result?.requiresRestart) {
      requiresRestart.value = true
    }
  } catch (e) {
    ElMessage.error('Не удалось изменить статус модуля')
  }
}
</script>
