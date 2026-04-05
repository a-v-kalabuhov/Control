import apiClient from './client'

/**
 * API-модуль для работы с персоналом (наладчиками)
 */
export const personnelApi = {
  /**
   * Получить список персонала
   * @param {Object} params - Параметры фильтрации
   * @param {boolean|null} params.isActive - Фильтр по статусу активности
   * @param {string|null} params.role - Фильтр по роли (Admin, Manager, Adjuster, Observer)
   * @returns {Promise<AxiosResponse>}
   */
  getList(params = {}) {
    return apiClient.get('/personnel', { params })
  },

  /**
   * Получить детали сотрудника по ID
   * @param {string} id - UUID сотрудника
   * @returns {Promise<AxiosResponse>}
   */
  getById(id) {
    return apiClient.get(`/personnel/${id}`)
  },

  /**
   * Создать нового сотрудника
   * @param {Object} data - Данные сотрудника
   * @param {string} data.employeeId - Табельный номер
   * @param {string} data.fullName - ФИО
   * @param {string} data.qualification - Квалификация
   * @param {string} data.login - Логин
   * @param {string} data.password - Пароль
   * @param {string} data.role - Роль (Admin, Manager, Adjuster, Observer)
   * @returns {Promise<AxiosResponse>}
   */
  create(data) {
    return apiClient.post('/personnel', data)
  },

  /**
   * Обновить данные сотрудника
   * @param {string} id - UUID сотрудника
   * @param {Object} data - Данные для обновления
   * @param {string} data.fullName - ФИО
   * @param {string} data.qualification - Квалификация
   * @param {boolean} data.isActive - Статус активности
   * @returns {Promise<AxiosResponse>}
   */
  update(id, data) {
    return apiClient.put(`/personnel/${id}`, data)
  },

  /**
   * Деактивировать сотрудника (мягкое удаление)
   * @param {string} id - UUID сотрудника
   * @returns {Promise<AxiosResponse>}
   */
  async deactivate(id) {
    return apiClient.put(`/personnel/${id}`, { isActive: false })
  },

  /**
   * Получить список наладчиков (для выпадающих списков)
   * @returns {Promise<AxiosResponse>}
   */
  getAdjusters() {
    return apiClient.get('/personnel', { 
      params: { 
        role: 'Adjuster', 
        isActive: true 
      } 
    })
  },

  /**
   * Получить список руководителей (для выдачи заданий)
   * @returns {Promise<AxiosResponse>}
   */
  getManagers() {
    return apiClient.get('/personnel', { 
      params: { 
        role: ['Manager', 'Admin'], 
        isActive: true 
      } 
    })
  }
}

export default personnelApi