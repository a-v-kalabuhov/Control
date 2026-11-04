<template>
  <el-dialog
    v-model="visible"
    title="Замена ингредиента"
    width="560px"
    :close-on-click-modal="false"
  >
    <div class="text-sm text-gray-600 mb-3">
      Строка: <span class="font-medium">{{ ingredient?.materialArticle }} — {{ ingredient?.materialName }}
      ({{ formatPercent(ingredient?.percent) }} %)</span>
    </div>
    <div class="text-sm text-gray-600 mb-3">
      Исходный материал: {{ ingredient?.originalMaterialArticle }} — {{ ingredient?.originalMaterialName }}
    </div>
    <el-select
      v-model="materialId"
      filterable
      placeholder="Выберите материал"
      class="w-full"
      :loading="loading"
      @focus="loadMaterials"
    >
      <el-option
        v-for="m in options"
        :key="m.id"
        :label="label(m)"
        :value="m.id"
      />
    </el-select>

    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button class="replace-save" type="primary" :disabled="!materialId" :loading="saving" @click="save">
        Заменить
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { materialsApi } from '@/api/materials'
import { tasksApi } from '@/api/tasks'
import { apiErrorMessage } from '@/utils/apiError'
import { formatPercent } from '@/utils/recipe'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  task: { type: Object, default: null },
  ingredient: { type: Object, default: null }
})

const emit = defineEmits(['update:modelValue', 'replaced'])

const visible = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
})

const materialId = ref(null)
const materials = ref([])
const loading = ref(false)
const saving = ref(false)

watch(() => [props.modelValue, props.ingredient], () => {
  if (props.modelValue) materialId.value = null
})

// Материалы того же типа, что и текущий ингредиент; исходный помечен «(исходный)».
const options = computed(() => {
  const list = materials.value.filter(m => m.type === props.ingredient?.materialType)
  const originalId = props.ingredient?.originalMaterialId
  return list.map(m => ({ ...m, isOriginal: m.id === originalId }))
})

function label(m) {
  const base = [m.article, m.name].filter(Boolean).join(' — ')
  return m.isOriginal ? `${base} (исходный)` : base
}

async function loadMaterials() {
  loading.value = true
  try {
    const { data } = await materialsApi.getList({
      type: props.ingredient?.materialType,
      isActive: true
    })
    materials.value = data
  } catch {
    ElMessage.error('Ошибка загрузки материалов')
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!materialId.value) return
  saving.value = true
  try {
    const { data } = await tasksApi.replaceRecipeIngredient(props.task.id, props.ingredient.id, materialId.value)
    emit('replaced', data)
    visible.value = false
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка замены материала'))
  } finally {
    saving.value = false
  }
}
</script>
