<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник персонала</h2>
        <p class="text-gray-600 mt-1">Управление персоналом и доступом в систему</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить сотрудника
      </el-button>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="ФИО или табельный номер" clearable />
        </el-form-item>
        <el-form-item label="Роль">
          <el-select v-model="filters.role" placeholder="Все" clearable style="width: 140px;">
            <el-option label="Все роли" value="" />
            <el-option label="Наладчик" value="Adjuster" />
            <el-option label="Литейщик" value="Operator" />
            <el-option label="Старший смены" value="ShiftLead" />
            <el-option label="Менеджер" value="Manager" />
            <el-option label="Админ" value="Admin" />
            <el-option label="Наблюдатель" value="Observer" />
          </el-select>
        </el-form-item>
        <el-form-item label="Статус">
          <el-select v-model="filters.isActive" placeholder="Все" clearable style="width: 160px;">
            <el-option label="Активные" :value="true" />
            <el-option label="Не активные" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadPersonnel">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Таблица персонала -->
    <el-table :data="personnelList" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="employeeId" label="Таб. номер" width="120" />
      <el-table-column prop="fullName" label="ФИО" />
      <el-table-column prop="qualification" label="Квалификация" width="150" />
      <el-table-column prop="role" label="Роль" width="140">
        <template #default="{ row }">
          <el-tag :type="getRoleType(row.role)">
            {{ getRoleLabel(row.role) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Вход в систему" width="140" align="center">
        <template #default="{ row }">
          <el-tag :type="row.login ? 'success' : 'info'">
            {{ row.login ? 'Есть' : 'Нет' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Статус" width="100" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активен' : 'Не активен' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="createdAt" label="Создан" width="160">
        <template #default="{ row }">
          {{ formatDate(row.createdAt) }}
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="220" fixed="right">
        <template #default="{ row }">
          <div class="flex flex-col items-start gap-1">
            <!-- Учётку администратора меняет только администратор (сервер вернёт 403) -->
            <template v-if="canManage(row)">
              <el-button size="small" style="width: 130px; margin: 0" @click="editPersonnel(row)">Редактировать</el-button>
              <el-button size="small" type="danger" style="width: 130px; margin: 0" @click="deletePersonnel(row)">Деактивировать</el-button>
            </template>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <!-- Модальное окно -->
    <el-dialog
      v-model="dialogVisible"
      :title="editingPersonnel ? 'Редактирование сотрудника' : 'Новый сотрудник'"
      width="600px"
    >
      <el-form :model="form" label-width="140px" :rules="rules" ref="formRef">
        <el-form-item label="Табельный номер" prop="employeeId" required>
          <el-input v-model="form.employeeId" placeholder="EMP-001" />
        </el-form-item>

        <el-form-item label="ФИО" prop="fullName" required>
          <el-input v-model="form.fullName" placeholder="Иванов Иван Иванович" />
        </el-form-item>

        <el-form-item label="Квалификация">
          <el-input v-model="form.qualification" placeholder="Слесарь-наладчик 4 разряда" />
        </el-form-item>

        <el-form-item label="Логин" prop="login">
          <el-input v-model="form.login" placeholder="Без логина вход запрещён" />
        </el-form-item>

        <el-form-item
          :label="editingPersonnel ? 'Новый пароль' : 'Пароль'"
          prop="password"
          :required="!!form.login.trim() && (!editingPersonnel || !editingPersonnel.login)"
        >
          <el-input
            v-model="form.password"
            type="password"
            :placeholder="editingPersonnel ? 'Оставьте пустым, чтобы не менять' : '••••••'"
          />
        </el-form-item>

        <el-form-item label="Роль" prop="role" required>
          <el-select v-model="form.role" placeholder="Выберите роль" class="w-full">
            <el-option label="Наладчик" value="Adjuster" />
            <el-option label="Литейщик" value="Operator" />
            <el-option label="Старший смены" value="ShiftLead" />
            <el-option label="Менеджер" value="Manager" />
            <el-option v-if="isAdmin" label="Администратор" value="Admin" />
            <el-option label="Наблюдатель" value="Observer" />
          </el-select>
        </el-form-item>

        <el-form-item label="Статус">
          <el-switch v-model="form.isActive" active-text="Активен" inactive-text="Не активен" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="savePersonnel" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { personnelApi } from '@/api/personnel'
import { usePermissions } from '@/composables/usePermissions'
import dayjs from 'dayjs'

const { isAdmin: hasAdminRole } = usePermissions()
const isAdmin = hasAdminRole()
// Руководитель управляет персоналом, кроме администраторов
const canManage = (person) => isAdmin || person.role !== 'Admin'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editingPersonnel = ref(null)
const formRef = ref(null)

const personnelList = ref([])

const filters = reactive({
  search: '',
  role: '',
  isActive: null
})

const form = reactive({
  employeeId: '',
  fullName: '',
  qualification: '',
  login: '',
  password: '',
  role: '',
  isActive: true
})

const validatePassword = (rule, value, callback) => {
  const grantingAccess = !!form.login.trim() && (!editingPersonnel.value || !editingPersonnel.value.login)
  if (grantingAccess && !value) {
    callback(new Error('При указании логина введите пароль'))
  } else {
    callback()
  }
}

const rules = {
  employeeId: [{ required: true, message: 'Введите табельный номер', trigger: 'blur' }],
  fullName: [{ required: true, message: 'Введите ФИО', trigger: 'blur' }],
  password: [{ validator: validatePassword, trigger: 'blur' }],
  role: [{ required: true, message: 'Выберите роль', trigger: 'change' }]
}

onMounted(() => {
  loadPersonnel()
})

const loadPersonnel = async () => {
  loading.value = true
  try {
    const response = await personnelApi.getList({ 
      search: filters.search,
      role: filters.role || undefined,
      isActive: filters.isActive 
    })
    personnelList.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки персонала')
    console.error(error)
  } finally {
    loading.value = false
  }
}

const showCreateModal = () => {
  editingPersonnel.value = null
  Object.assign(form, {
    employeeId: '',
    fullName: '',
    qualification: '',
    login: '',
    password: '',
    role: '',
    isActive: true
  })
  dialogVisible.value = true
}

const editPersonnel = (person) => {
  editingPersonnel.value = person
  Object.assign(form, {
    employeeId: person.employeeId,
    fullName: person.fullName,
    qualification: person.qualification || '',
    login: person.login || '',
    role: person.role,
    isActive: person.isActive,
    password: '' // не показываем пароль
  })
  dialogVisible.value = true
}

const savePersonnel = async () => {
  if (!formRef.value) return
  
  await formRef.value.validate(async (valid) => {
    if (!valid) return

    saving.value = true
    try {
      const payload = { ...form, login: form.login.trim() }
      if (editingPersonnel.value) {
        if (!payload.password) delete payload.password // пустой новый пароль не меняет текущий
        await personnelApi.update(editingPersonnel.value.id, payload)
        ElMessage.success('Сотрудник обновлён')
      } else {
        await personnelApi.create(payload)
        ElMessage.success('Сотрудник создан')
      }

      dialogVisible.value = false
      await loadPersonnel()
    } catch (error) {
      let errorMessage = 'Ошибка сохранения сотрудника'
      
      if (error.response?.data) {
        const data = error.response.data
        if (Array.isArray(data)) {
          // Identity Errors collection from BadRequest(result.Errors)
          errorMessage += ': ' + data.map(err => err.description || err.code || JSON.stringify(err)).join('; ')
        } else if (typeof data === 'string') {
          errorMessage += ': ' + data
        } else if (data.errors && Array.isArray(data.errors)) {
          errorMessage += ': ' + data.errors.join('; ')
        } else if (data.message) {
          errorMessage += ': ' + data.message
        } else if (data.title) {
          errorMessage += ': ' + data.title
        } else {
          errorMessage += ': ' + JSON.stringify(data)
        }
      } else if (error.message) {
        errorMessage += ': ' + error.message
      }
      
      ElMessage.error(errorMessage)
      console.error('Ошибка сохранения сотрудника:', error)
    } finally {
      saving.value = false
    }
  })
}

const deletePersonnel = async (person) => {
  try {
    await ElMessageBox.confirm(`Деактивировать сотрудника "${person.fullName}"?`, 'Подтверждение', {
      type: 'warning'
    })
    
    await personnelApi.update(person.id, { isActive: false })
    ElMessage.success('Сотрудник деактивирован')
    await loadPersonnel()
  } catch (error) {
    if (error !== 'cancel') {
      ElMessage.error('Ошибка деактивации')
    }
  }
}

const getRoleLabel = (role) => {
  const labels = {
    Admin: 'Админ',
    Manager: 'Менеджер',
    Adjuster: 'Наладчик',
    Operator: 'Литейщик',
    ShiftLead: 'Старший смены',
    Observer: 'Наблюдатель'
  }
  return labels[role] || role
}

const getRoleType = (role) => {
  if (role === 'Admin' || role === 'Manager') return 'primary'
  if (role === 'Adjuster') return 'success'
  return 'info'
}

const formatDate = (date) => {
  return dayjs(date).format('DD.MM.YYYY HH:mm')
}
</script>
