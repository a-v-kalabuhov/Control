<template>
  <div class="min-h-screen flex items-center justify-center bg-gradient-to-br from-primary-50 to-primary-100">
    <div class="card w-full max-w-md p-8">
      <!-- Логотип -->
      <div class="text-center mb-8">
        <div class="text-3xl font-bold text-primary-700 mb-2">CONTROL</div>
        <p class="text-gray-600">Управление цехом ТПА</p>
      </div>

      <!-- Форма входа -->
      <el-form
        ref="formRef"
        :model="formData"
        :rules="rules"
        label-position="top"
        @submit.prevent="handleLogin"
      >
        <el-form-item label="Логин" prop="login">
          <el-input
            v-model="formData.login"
            placeholder="Введите логин"
            prefix-icon="User"
            size="large"
          />
        </el-form-item>

        <el-form-item label="Пароль" prop="password">
          <el-input
            v-model="formData.password"
            type="password"
            placeholder="Введите пароль"
            prefix-icon="Lock"
            size="large"
            show-password
            @keyup.enter="handleLogin"
          />
        </el-form-item>

        <el-form-item>
          <el-button
            type="primary"
            size="large"
            :loading="loading"
            class="w-full btn-primary"
            @click="handleLogin"
          >
            {{ loading ? 'Вход...' : 'Войти' }}
          </el-button>
        </el-form-item>
      </el-form>

      <!-- Сообщение об ошибке -->
      <el-alert
        v-if="errorMessage"
        type="error"
        :title="errorMessage"
        show-icon
        closable
        class="mt-4"
      />

      <!-- Демо-учётки (для разработки) -->
      <div class="mt-6 p-4 bg-gray-50 rounded-lg text-sm">
        <p class="font-medium text-gray-700 mb-2">📋 Демо-учётки:</p>
        <div class="space-y-1 text-gray-600">
          <div><span class="font-mono">admin</span> / Admin123! (Администратор)</div>
          <div><span class="font-mono">manager</span> / Manager123! (Руководитель)</div>
          <div><span class="font-mono">adjuster</span> / Adjuster123! (Наладчик)</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { ElMessage } from 'element-plus'

const router = useRouter()
const authStore = useAuthStore()

const formRef = ref(null)
const loading = ref(false)
const errorMessage = ref('')

const formData = reactive({
  login: '',
  password: ''
})

const rules = {
  login: [
    { required: true, message: 'Введите логин', trigger: 'blur' }
  ],
  password: [
    { required: true, message: 'Введите пароль', trigger: 'blur' },
    { min: 6, message: 'Пароль должен быть не менее 6 символов', trigger: 'blur' }
  ]
}

const handleLogin = async () => {
  if (!formRef.value) return
  
  await formRef.value.validate(async (valid) => {
    if (!valid) return
    
    loading.value = true
    errorMessage.value = ''
    
    const result = await authStore.login(formData)
    
    loading.value = false
    
    if (result.success) {
      ElMessage.success(`Добро пожаловать, ${authStore.user?.fullName}!`)
      
      // Редирект в зависимости от роли
      if (authStore.isAdjuster) {
        router.push('/mobile/tasks')
      } else {
        router.push('/')
      }
    } else {
      errorMessage.value = result.message
      ElMessage.error(result.message)
    }
  })
}
</script>

<style scoped>
:deep(.el-input__wrapper) {
  @apply rounded-lg;
}

:deep(.el-button--primary) {
  @apply bg-primary-600 border-primary-600;
}

:deep(.el-button--primary:hover) {
  @apply bg-primary-700 border-primary-700;
}
</style>