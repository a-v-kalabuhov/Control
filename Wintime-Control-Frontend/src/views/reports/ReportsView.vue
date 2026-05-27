<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6">
      <h2 class="text-2xl font-bold text-gray-800">Отчёты</h2>
      <p class="text-gray-600 mt-1">Аналитические отчёты по производству с экспортом в Excel</p>
    </div>

    <!-- Выбор типа отчёта -->
    <el-card class="mb-6">
      <template #header>
        <span class="font-semibold">Выберите тип отчёта</span>
      </template>

      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div 
          class="card cursor-pointer hover:shadow-lg transition-all"
          :class="selectedType === 'daily' ? 'ring-2 ring-primary-500' : ''"
          @click="selectedType = 'daily'"
        >
          <div class="flex items-center gap-4">
            <div class="p-3 bg-blue-100 rounded-lg">
              <el-icon class="text-blue-600 text-2xl"><Calendar /></el-icon>
            </div>
            <div>
              <h3 class="font-semibold text-gray-800">Картина рабочего дня</h3>
              <p class="text-sm text-gray-500">За смену по каждому ТПА</p>
            </div>
          </div>
        </div>

        <div 
          class="card cursor-pointer hover:shadow-lg transition-all"
          :class="selectedType === 'equipment' ? 'ring-2 ring-primary-500' : ''"
          @click="selectedType = 'equipment'"
        >
          <div class="flex items-center gap-4">
            <div class="p-3 bg-green-100 rounded-lg">
              <el-icon class="text-green-600 text-2xl"><TrendCharts /></el-icon>
            </div>
            <div>
              <h3 class="font-semibold text-gray-800">Производительность оборудования</h3>
              <p class="text-sm text-gray-500">За период по цеху</p>
            </div>
          </div>
        </div>

        <div 
          class="card cursor-pointer hover:shadow-lg transition-all"
          :class="selectedType === 'assets' ? 'ring-2 ring-primary-500' : ''"
          @click="selectedType = 'assets'"
        >
          <div class="flex items-center gap-4">
            <div class="p-3 bg-purple-100 rounded-lg">
              <el-icon class="text-purple-600 text-2xl"><Box /></el-icon>
            </div>
            <div>
              <h3 class="font-semibold text-gray-800">Активы цеха</h3>
              <p class="text-sm text-gray-500">Пресс-формы и наладчики</p>
            </div>
          </div>
        </div>
      </div>

      <div class="mt-4 flex justify-end">
        <el-button 
          type="primary" 
          :disabled="!selectedType"
          @click="openReport"
        >
          Открыть отчёт
        </el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'

const router = useRouter()
const selectedType = ref(null)

const openReport = () => {
  if (!selectedType.value) return
  router.push(`/reports/${selectedType.value}`)
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4 border border-gray-200;
}
</style>