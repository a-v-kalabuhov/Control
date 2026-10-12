// PZP-14: план задания — наладка → выпуск → буфер выпуска.
// Та же формула, что TaskPlanCalculator на бэкенде; здесь — только для живого предпросмотра
// в форме. В карточке задания показываем то, что посчитал сервер (TaskDto).

export function calcTaskPlan({ planQuantity, cavities, fullCycleSeconds, setupMinutes, bufferFixedMinutes, bufferPercent }) {
  const setupSeconds = (setupMinutes ?? 0) * 60
  if (!fullCycleSeconds || !cavities || cavities <= 0 || !planQuantity) {
    return { setupSeconds, releaseSeconds: null, bufferSeconds: null, totalSeconds: null }
  }
  const releaseSeconds = Math.ceil(planQuantity / cavities) * fullCycleSeconds
  const bufferSeconds = (bufferFixedMinutes ?? 0) * 60 + Math.ceil(releaseSeconds * (bufferPercent ?? 0) / 100)
  return { setupSeconds, releaseSeconds, bufferSeconds, totalSeconds: setupSeconds + releaseSeconds + bufferSeconds }
}

// Длительность для человека: минуты округляются вверх.
export function formatDuration(seconds) {
  if (seconds === null || seconds === undefined) return '—'
  const totalMinutes = Math.ceil(seconds / 60)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  if (hours === 0) return `${minutes} мин`
  if (minutes === 0) return `${hours} ч`
  return `${hours} ч ${minutes} мин`
}
