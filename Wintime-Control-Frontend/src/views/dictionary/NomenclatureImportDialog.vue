<template>
  <el-dialog
    :model-value="modelValue"
    title="Импорт номенклатуры из Excel"
    width="900px"
    :close-on-click-modal="!loading"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <el-alert type="info" :closable="false" show-icon>
        <template #title>Формат XLSX: колонки A–L. Макросы и внешние связи не выполняются.</template>
        Сначала файл проверяется. При ошибочных строках изменения в БД не сохраняются.
      </el-alert>

      <div class="flex items-center gap-3">
        <input
          ref="fileInput"
          data-test="nomen-file"
          type="file"
          accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          :disabled="loading"
          @change="onFileSelected"
        />
        <el-button
          v-if="!result || result.status === 'ConfirmationRequired'"
          type="primary"
          :disabled="!selectedFile"
          :loading="loading"
          @click="upload(false)"
        >
          Проверить и импортировать
        </el-button>
      </div>
      <div v-if="selectedFile" class="text-sm text-gray-600">{{ selectedFile.name }}</div>

      <template v-if="result">
        <el-alert
          :type="result.status === 'Imported' ? 'success' : 'warning'"
          :closable="false"
          show-icon
        >
          <template #title>
            <span v-if="result.status === 'Imported'">
              Импортировано строк: {{ result.importedRows }}; пропущено: {{ result.skippedRows }}.
            </span>
            <span v-else>Найдены ошибочные записи. В БД ничего не изменено.</span>
          </template>
          <span v-if="result.status === 'Imported' && result.report?.length">
            Импорт завершён, но есть замечания для ручной доработки.
          </span>
          <span v-else-if="result.status === 'ConfirmationRequired'">
            Исправьте файл или пропустите ошибочные строки. Замечания по рецептам не блокируют импорт.
          </span>
        </el-alert>

        <el-table v-if="result.report?.length" :data="result.report" max-height="360" size="small" stripe>
          <el-table-column prop="rowNumber" label="Строка" width="80" align="right" />
          <el-table-column prop="fullArticle" label="Артикул" width="180" />
          <el-table-column label="Тип" width="110" align="center">
            <template #default="{ row }">
              <el-tag :type="row.blocksRow ? 'danger' : 'warning'" size="small">
                {{ row.blocksRow ? 'Ошибка' : 'Замечание' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="message" label="Сообщение" min-width="350" />
        </el-table>
      </template>
    </div>

    <template #footer>
      <div class="flex justify-between">
        <div>
          <el-button v-if="result?.report?.length" @click="downloadReport">Сохранить отчёт CSV</el-button>
          <el-button v-if="result" :disabled="loading" @click="chooseAnotherFile">Загрузить исправленный файл</el-button>
        </div>
        <div>
          <el-button :disabled="loading" @click="$emit('update:modelValue', false)">
            {{ result?.status === 'Imported' ? 'Закрыть' : 'Отмена' }}
          </el-button>
          <el-button
            v-if="result?.status === 'ConfirmationRequired'"
            type="warning"
            :loading="loading"
            @click="upload(true)"
          >
            Пропустить ошибочные и импортировать
          </el-button>
        </div>
      </div>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { nomenclatureImportApi } from '@/api/nomenclatureImport'
import { apiErrorMessage } from '@/utils/apiError'
import { downloadNomenclatureReportCsv } from '@/utils/nomenclatureImportReport'

const props = defineProps({ modelValue: { type: Boolean, default: false } })
const emit = defineEmits(['update:modelValue', 'imported'])

const fileInput = ref(null)
const selectedFile = ref(null)
const result = ref(null)
const loading = ref(false)
let requestId = 0

function resetImport() {
  requestId += 1
  selectedFile.value = null
  result.value = null
  loading.value = false
  if (fileInput.value) fileInput.value.value = ''
}

watch(() => props.modelValue, visible => {
  if (!visible) resetImport()
})

function onFileSelected(event) {
  selectedFile.value = event.target.files?.[0] ?? null
  result.value = null
}

async function upload(skipInvalidRows) {
  if (!selectedFile.value || loading.value) return
  const currentRequest = ++requestId
  loading.value = true
  try {
    const response = await nomenclatureImportApi.upload(selectedFile.value, skipInvalidRows)
    if (currentRequest !== requestId) return
    result.value = response.data
    if (result.value.status === 'Imported') {
      ElMessage.success(`Импортировано строк: ${result.value.importedRows}`)
      emit('imported', result.value)
    }
  } catch (error) {
    if (currentRequest === requestId)
      ElMessage.error(apiErrorMessage(error, 'Ошибка проверки или импорта файла'))
  } finally {
    if (currentRequest === requestId) loading.value = false
  }
}

function downloadReport() {
  if (result.value?.report?.length)
    downloadNomenclatureReportCsv(result.value.report)
}

function chooseAnotherFile() {
  resetImport()
  fileInput.value?.click()
}
</script>
