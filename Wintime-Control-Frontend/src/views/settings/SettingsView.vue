<template>
  <div>
    <div class="mb-6">
      <h2 class="text-2xl font-bold text-gray-800">Настройки</h2>
    </div>

    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Умолчания планирования</span>
      </template>

      <p class="text-sm text-gray-600 mb-4">
        Значения подставляются в новые задания. Уже созданные задания не меняются.
      </p>

      <el-form v-loading="loading" :model="defaults" label-width="340px" label-position="left">
        <el-form-item label="Норматив наладки, мин">
          <el-input-number
            v-model="defaults.setupMinutes"
            data-test="setup-minutes" :value-on-clear="0"
            :min="0" :max="1440" :step="5"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Буфер выпуска: фиксированная часть, мин">
          <el-input-number
            v-model="defaults.releaseBufferFixedMinutes"
            data-test="buffer-fixed-minutes" :value-on-clear="0"
            :min="0" :max="1440" :step="5"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Буфер выпуска: % от времени выпуска">
          <el-input-number
            v-model="defaults.releaseBufferPercent"
            data-test="buffer-percent" :value-on-clear="0"
            :min="0" :max="100"
            controls-position="right"
          />
        </el-form-item>
      </el-form>
    </el-card>

    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Безопасность</span>
      </template>
      <p class="text-sm text-gray-600 mb-4">
        Настройка применяется при создании пользователей и смене паролей. Существующие пароли не меняются.
      </p>
      <el-form :model="security" label-position="left">
        <el-form-item>
          <el-checkbox v-model="security.allowWeakPasswords" data-test="allow-weak-passwords">
            Позволять слабые пароли
          </el-checkbox>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Конфликты плана</span>
      </template>
      <p class="text-sm text-gray-600 mb-4">
        Пересечение двух заданий по ТПА или пресс-форме не дольше порога конфликтом не считается.
        Действует сразу для всех заданий.
      </p>
      <el-form :model="defaults" label-width="340px" label-position="left">
        <el-form-item label="Порог конфликта, мин">
          <el-input-number
            v-model="defaults.conflictThresholdMinutes"
            data-test="conflict-threshold" :value-on-clear="0"
            :min="0" :max="1440" :step="5"
            controls-position="right"
          />
        </el-form-item>
      </el-form>
    </el-card>

    <div class="flex justify-end gap-4">
      <el-button @click="load">Отмена</el-button>
      <el-button type="primary" data-test="save" :loading="saving" @click="save">Сохранить</el-button>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { settingsApi } from '@/api/settings'
import { usePlanConflictsStore } from '@/stores/planConflicts'
import { apiErrorMessage } from '@/utils/apiError'

const loading = ref(false)
const saving = ref(false)

const defaults = reactive({
  setupMinutes: 0,
  releaseBufferFixedMinutes: 0,
  releaseBufferPercent: 0,
  conflictThresholdMinutes: 20
})

const security = reactive({
  allowWeakPasswords: false
})

const load = async () => {
  loading.value = true
  try {
    const [{ data: planningData }, { data: securityData }] = await Promise.all([
      settingsApi.getPlanningDefaults(),
      settingsApi.getSecuritySettings()
    ])
    defaults.setupMinutes = planningData.setupMinutes
    defaults.releaseBufferFixedMinutes = planningData.releaseBufferFixedMinutes
    defaults.releaseBufferPercent = planningData.releaseBufferPercent
    defaults.conflictThresholdMinutes = planningData.conflictThresholdMinutes
    security.allowWeakPasswords = securityData.allowWeakPasswords
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка загрузки настроек'))
  } finally {
    loading.value = false
  }
}

const save = async () => {
  saving.value = true
  try {
    await settingsApi.updatePlanningDefaults({
      setupMinutes: defaults.setupMinutes,
      releaseBufferFixedMinutes: defaults.releaseBufferFixedMinutes,
      releaseBufferPercent: defaults.releaseBufferPercent,
      conflictThresholdMinutes: defaults.conflictThresholdMinutes
    })
    await settingsApi.updateSecuritySettings({
      allowWeakPasswords: security.allowWeakPasswords
    })
    usePlanConflictsStore().load() // порог действует сразу на все задания
    ElMessage.success('Настройки сохранены')
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка сохранения настроек'))
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
