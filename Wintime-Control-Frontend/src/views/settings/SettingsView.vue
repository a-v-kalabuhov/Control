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
import { apiErrorMessage } from '@/utils/apiError'

const loading = ref(false)
const saving = ref(false)

const defaults = reactive({
  setupMinutes: 0,
  releaseBufferFixedMinutes: 0,
  releaseBufferPercent: 0
})

const load = async () => {
  loading.value = true
  try {
    const { data } = await settingsApi.getPlanningDefaults()
    defaults.setupMinutes = data.setupMinutes
    defaults.releaseBufferFixedMinutes = data.releaseBufferFixedMinutes
    defaults.releaseBufferPercent = data.releaseBufferPercent
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
      releaseBufferPercent: defaults.releaseBufferPercent
    })
    ElMessage.success('Настройки сохранены')
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка сохранения настроек'))
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
