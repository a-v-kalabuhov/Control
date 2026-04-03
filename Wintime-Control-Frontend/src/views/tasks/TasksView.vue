<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Диспетчерская</h2>
        <p class="text-gray-600 mt-1">Управление сменно-суточными заданиями</p>
      </div>
      <el-button type="primary" @click="showCreateModal" v-if="canCreate">
        <el-icon class="mr-1"><Plus /></el-icon>
        Новое задание
      </el-button>
    </div>

    <!-- Статистика -->
    <div class="grid grid-cols-1 md:grid-cols-5 gap-4 mb-6">
      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-blue-100 rounded-lg">
            <el-icon class="text-blue-600 text-xl"><Document /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Всего</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.tasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-gray-100 rounded-lg">
            <el-icon class="text-gray-600 text-xl"><Edit /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Черновики</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.draftTasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-yellow-100 rounded-lg">
            <el-icon class="text-yellow-600 text-xl"><Clock /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">В работе</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.inProgressTasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-red-100 rounded-lg">
            <el-icon class="text-red-600 text-xl"><Warning /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Просрочено</p>
            <p class="text-2xl font-bold text-gray-800">{{ tasksStore.overdueTasks.length }}</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-green-100 rounded-lg">
            <el-icon class="text-green-600 text-xl"><TrendCharts /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Эффективность</p>
            <p class="text-2xl font-bold text-green-600">{{ tasksStore.overallProgress }}%</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Фильтры -->
    <TaskFilters 
      :filters="tasksStore.filters"
      @update:filters="(filters) => tasksStore.setFilter(Object.keys(filters)[0], filters[Object.keys(filters)[0]])"
      @apply="loadTasks"
    />

    <!-- Таблица заданий -->
    <el-card>
      <el-table 
        :data="tasksStore.filteredTasks" 
        stripe 
        style="width: 100%" 
        v-loading="tasksStore.loading"
        @row-click="openDetail"
        class="cursor-pointer"
      >
        <el-table-column prop="immName" label="ТПА" width="150" />
        <el-table-column prop="moldName" label="Пресс-форма" min-width="200" />
        <el-table-column prop="personnelName" label="Наладчик" width="180" />
        
        <el-table-column label="План / Факт" width="150">
          <template #default="{ row }">
            <div class="text-sm">
              <div class="font-medium">{{ row.actualQuantity || 0 }} / {{ row.planQuantity }}</div>
              <div class="text-gray-500 text-xs">шт.</div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="Прогресс" width="180">
          <template #default="{ row }">
            <TaskProgress 
              :plan-quantity="row.planQuantity" 
              :actual-quantity="row.actualQuantity || 0" 
            />
          </template>
        </el-table-column>

        <el-table-column prop="status" label="Статус" width="120">
          <template #default="{ row }">
            <TaskStatusBadge :status="row.status" />
          </template>
        </el-table-column>

        <el-table-column prop="issuedAt" label="Выдано" width="160">
          <template #default="{ row }">
            {{ formatDate(row.issuedAt) }}
          </template>
        </el-table-column>

        <el-table-column label="Действия" width="200" fixed="right">
          <template #default="{ row }">
            <el-button 
              size="small" 
              @click.stop="openDetail(row)"
            >
              Детали
            </el-button>
            <el-button 
              v-if="canEditTask(row)"
              size="small" 
              type="primary"
              @click.stop="editTask(row)"
            >
              Ред.
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Модальные окна -->
    <TaskFormModal
      v-model="formModalVisible"
      :task="editingTask"
      @success="loadTasks"
    />

    <TaskDetailModal
      v-model="detailModalVisible"
      :task="selectedTask"
      @edit="editTask"
      @complete="completeTask"
      @close="closeTask"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useTasksStore } from '@/stores/tasks'
import { useAuthStore } from '@/stores/auth'
import TaskFilters from '@/components/tasks/TaskFilters.vue'
import TaskStatusBadge from '@/components/tasks/TaskStatusBadge.vue'
import TaskProgress from '@/components/tasks/TaskProgress.vue'
import TaskFormModal from './TaskFormModal.vue'
import TaskDetailModal from './TaskDetailModal.vue'
import dayjs from 'dayjs'

const tasksStore = useTasksStore()
const authStore = useAuthStore()

const formModalVisible = ref(false)
const detailModalVisible = ref(false)
const editingTask = ref(null)
const selectedTask = ref(null)

const canCreate = computed(() => authStore.isManager || authStore.isAdmin)

onMounted(() => {
  loadTasks()
})

const loadTasks = async () => {
  await tasksStore.loadTasks()
}

const showCreateModal = () => {
  editingTask.value = null
  formModalVisible.value = true
}

const editTask = (task) => {
  editingTask.value = task
  formModalVisible.value = true
}

const openDetail = (task) => {
  selectedTask.value = task
  detailModalVisible.value = true
}

const completeTask = async (task) => {
  try {
    await ElMessageBox.prompt('Укажите фактическое количество (если отличается от плана)', 'Завершение задания', {
      inputPattern: /^\d+$/,
      inputErrorMessage: 'Введите число'
    })

    const { value } = await ElMessageBox.prompt('Причина (если план не выполнен)', 'Причина', {
      inputPattern: /.+/,
      inputErrorMessage: 'Введите причину'
    })

    await tasksStore.completeTask(task.id, {
      actualQuantity: parseInt(value),
      completionReason: value
    })
    
    detailModalVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка завершения задания')
    }
  }
}

const closeTask = async (task) => {
  try {
    await ElMessageBox.confirm('Закрыть задание?', 'Подтверждение', {
      type: 'warning'
    })

    await tasksStore.closeTask(task.id, {})
    detailModalVisible.value = false
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка закрытия задания')
    }
  }
}

const canEditTask = (task) => {
  return task.status === 'Draft' && canCreate.value
}

const formatDate = (date) => {
  if (!date) return '—'
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}

:deep(.el-table__row) {
  @apply cursor-pointer;
}

:deep(.el-table__row:hover) {
  @apply bg-blue-50;
}
</style>