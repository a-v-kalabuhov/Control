export function useValidation() {
  
  // Проверка, настроен ли сенсор (базовые значения не дефолтные)
  const isSensorConfigured = (sensor) => {
    if (!sensor) return false
    
    switch (sensor.type) {
      case 'float':
        // Не настроен, если ВСЕ три значения = 0
        return !(sensor.baseValueAuto === 0 && 
                 sensor.baseValueManual === 0 && 
                 sensor.baseValueIdle === 0)
      case 'boolean':
        // Любое значение валидно
        return true
      case 'string':
        // Не настроен, если ВСЕ три значения = пустая строка
        return !(!sensor.stringValueAuto?.trim() && 
                 !sensor.stringValueManual?.trim() && 
                 !sensor.stringValueIdle?.trim())
      default:
        return true
    }
  }

  // Проверка всего инстанса перед запуском
  const validateEmulation = (profile, sensorConfigs) => {
    const errors = []

    // 1. Профиль должен иметь хотя бы 1 шаг
    if (!profile || profile.length === 0) {
      errors.push('Не задан профиль работы (минимум 1 шаг)')
    }

    // 2. Все сенсоры должны быть настроены
    const unconfigured = sensorConfigs?.filter(s => !isSensorConfigured(s)) || []
    if (unconfigured.length > 0) {
      errors.push(`Не настроены сенсоры: ${unconfigured.map(s => s.name).join(', ')}`)
    }

    return {
      isValid: errors.length === 0,
      errors
    }
  }

  return { isSensorConfigured, validateEmulation }
}