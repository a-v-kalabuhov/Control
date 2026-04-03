<template>
  <div>
    <div class="mb-6">
      <h2 class="text-2xl font-bold text-gray-800">Мониторинг оборудования</h2>
      <p class="text-gray-600 mt-1">Текущее состояние всех ТПА в реальном времени</p>
    </div>

    <!-- Статистика -->
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-green-100 rounded-lg">
            <el-icon class="text-green-600 text-xl"><CircleCheck /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">В работе</p>
            <p class="text-2xl font-bold text-gray-800">7</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-yellow-100 rounded-lg">
            <el-icon class="text-yellow-600 text-xl"><Clock /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Наладка</p>
            <p class="text-2xl font-bold text-gray-800">2</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-red-100 rounded-lg">
            <el-icon class="text-red-600 text-xl"><Warning /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Авария</p>
            <p class="text-2xl font-bold text-gray-800">1</p>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="flex items-center gap-4">
          <div class="p-3 bg-gray-100 rounded-lg">
            <el-icon class="text-gray-600 text-xl"><Switch /></el-icon>
          </div>
          <div>
            <p class="text-sm text-gray-500">Оффлайн</p>
            <p class="text-2xl font-bold text-gray-800">0</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Плитки ТПА (заглушка) -->
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
      <div 
        v-for="i in 10" 
        :key="i"
        class="card cursor-pointer hover:shadow-lg transition-shadow"
      >
        <div class="flex items-start justify-between mb-3">
          <div>
            <h3 class="font-semibold text-gray-800">ТПА-{{ String(i).padStart(2, '0') }}</h3>
            <p class="text-sm text-gray-500">Siger 200s1</p>
          </div>
          <el-tag 
            :type="i === 3 ? 'danger' : i === 2 ? 'warning' : 'success'"
            size="small"
          >
            {{ i === 3 ? 'Авария' : i === 2 ? 'Наладка' : 'Авто' }}
          </el-tag>
        </div>

        <div class="space-y-2 text-sm">
          <div class="flex justify-between">
            <span class="text-gray-500">Задание:</span>
            <span class="text-gray-800">КлипДак (48)</span>
          </div>
          <div class="flex justify-between">
            <span class="text-gray-500">Прогресс:</span>
            <span class="text-gray-800">{{ Math.floor(Math.random() * 100) }}%</span>
          </div>
          <div class="flex justify-between">
            <span class="text-gray-500">Циклов:</span>
            <span class="text-gray-800">{{ Math.floor(Math.random() * 10000) }}</span>
          </div>
        </div>

        <el-progress 
          :percentage="Math.floor(Math.random() * 100)" 
          :stroke-width="6"
          class="mt-3"
        />
      </div>
    </div>
  </div>
</template>

<script setup>
// Заглушка для демонстрации
// В Части 3 будет реальная загрузка данных ТПА
</script>