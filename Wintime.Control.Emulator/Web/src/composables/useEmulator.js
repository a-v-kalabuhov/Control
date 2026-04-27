import { emulatorApi } from '../api/client'
import { ElMessage } from 'element-plus'

export function useEmulator() {
  
  const startEmulation = async (immId, config) => {
    try {
      await emulatorApi.startEmulation({ immId, ...config })
      ElMessage.success(`Эмуляция запущена: ${immId}`)
      return true
    } catch (e) {
      ElMessage.error(`Ошибка запуска: ${e.response?.data?.message || e.message}`)
      return false
    }
  }

  const stopEmulation = async (immId) => {
    try {
      await emulatorApi.stopEmulation(immId)
      ElMessage.success(`Эмуляция остановлена: ${immId}`)
      return true
    } catch (e) {
      ElMessage.error(`Ошибка остановки: ${e.message}`)
      return false
    }
  }

  const loadPreset = async (immId) => {
    try {
      const response = await emulatorApi.getPreset(immId)
      return response.data
    } catch (e) {
      if (e.response?.status === 404) return null
      ElMessage.error(`Ошибка загрузки пресета: ${e.message}`)
      throw e
    }
  }

  const savePreset = async (immId, preset) => {
    try {
      await emulatorApi.savePreset(immId, { ...preset, immId })
      ElMessage.success('Пресет сохранён')
      return true
    } catch (e) {
      ElMessage.error(`Ошибка сохранения: ${e.message}`)
      return false
    }
  }

  const loadTemplateSensors = async (templateId) => {
    try {
      const response = await emulatorApi.getTemplate(templateId)
      // Преобразуем сенсоры шаблона в конфиги эмуляции с дефолтными значениями
      console.log('loadTemplateSensors', response.data);
      return response.data.sensors.map(s => ({
        name: s.name,
        type: s.type,
        // Дефолтные значения (требуют настройки пользователем)
        baseValueAuto: 0,
        baseValueManual: 0,
        baseValueIdle: 0,
        variancePercent: 0,
        valueAuto: false,
        valueManual: false,
        valueIdle: false,
        stringValueAuto: '',
        stringValueManual: '',
        stringValueIdle: ''
      }))
    } catch (e) {
      ElMessage.error(`Ошибка загрузки шаблона: ${e.message}`)
      throw e
    }
  }

  return {
    startEmulation,
    stopEmulation,
    loadPreset,
    savePreset,
    loadTemplateSensors
  }
}