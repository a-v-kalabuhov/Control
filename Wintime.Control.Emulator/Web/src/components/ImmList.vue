<template>
  <div class="imm-list">
    <div class="header">
      <h2>Список ТПА</h2>
      <el-button type="primary" @click="$emit('refresh')" :loading="loading">
        <el-icon><Refresh /></el-icon> Обновить
      </el-button>
    </div>

    <el-empty v-if="!loading && (!imms || imms.length === 0)" description="Нет доступных ТПА" />

    <el-row :gutter="20" v-else>
      <el-col :xs="24" :sm="12" :lg="8" v-for="imm in imms" :key="imm.id">
        <ImmCard 
          :imm="imm"
          :status="statuses[imm.id]"
          @configure="$emit('configure', imm)"
          @start="$emit('start', imm)"
          @stop="$emit('stop', imm)"
        />
      </el-col>
    </el-row>

    <el-skeleton :rows="5" animated v-if="loading" />
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import ImmCard from './ImmCard.vue'

const props = defineProps({
  imms: Array,
  statuses: Object,
  loading: Boolean
})

defineEmits(['refresh', 'configure', 'start', 'stop'])
</script>

<style scoped>
.imm-list { padding: 20px; }
.header { 
  display: flex; 
  justify-content: space-between; 
  align-items: center; 
  margin-bottom: 20px;
}
</style>