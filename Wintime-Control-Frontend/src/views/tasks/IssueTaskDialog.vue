<template>
  <el-dialog v-model="visible" title="Выдать задание" width="420px" :close-on-click-modal="false">
    <p v-if="task" class="mb-4 text-gray-600">{{ task.immName }} / {{ task.moldName }} / {{ task.productFullArticle }}</p>
    <el-form label-position="top">
      <el-form-item label="Наладчик" required>
        <el-select v-model="personnelId" placeholder="Выберите наладчика" filterable class="w-full">
          <el-option v-for="p in personnel" :key="p.id" :label="p.fullName" :value="p.id" />
        </el-select>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">Отмена</el-button>
      <el-button type="warning" :disabled="!canSubmit" :loading="loading" @click="submit">Выдать</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { personnelApi } from '@/api/personnel'
import { useTasksStore } from '@/stores/tasks'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  task: { type: Object, default: null }
})
const emit = defineEmits(['update:modelValue', 'issued'])

const tasksStore = useTasksStore()
const personnel = ref([])
const personnelId = ref(null)
const loading = ref(false)

const visible = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v)
})
const canSubmit = computed(() => !!personnelId.value && !loading.value)

async function open() {
  personnelId.value = null
  try {
    const res = await personnelApi.getList({ isActive: true, role: 'Adjuster' })
    personnel.value = res.data ?? []
  } catch {
    ElMessage.error('Не удалось загрузить список наладчиков')
    personnel.value = []
  }
  // Предзаполняем только активным наладчиком: архивного в списке нет.
  const assigned = props.task?.personnelId
  personnelId.value = personnel.value.some(p => p.id === assigned) ? assigned : null
}

watch(() => props.modelValue, (v) => { if (v) open() }, { immediate: true })

async function submit() {
  if (!props.task || !personnelId.value) return
  loading.value = true
  try {
    const result = await tasksStore.issueTask(props.task.id, personnelId.value)
    if (result?.success) {
      visible.value = false
      emit('issued')
    }
  } finally {
    loading.value = false
  }
}

defineExpose({ personnelId, canSubmit, submit })
</script>
