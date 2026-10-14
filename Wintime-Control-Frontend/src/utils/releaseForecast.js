// Прогноз окончания выпуска на плитке ТПА. Задание может идти через несколько суток,
// поэтому время без даты неоднозначно: «сегодня в ЧЧ:ММ», «завтра в ЧЧ:ММ», иначе
// «ДД.ММ в ЧЧ:ММ» (год — только если не текущий). Пояс — браузера.

const pad = (n) => String(n).padStart(2, '0')

const sameDay = (a, b) => a.getFullYear() === b.getFullYear()
  && a.getMonth() === b.getMonth()
  && a.getDate() === b.getDate()

export function formatForecastMoment(eta, now = new Date()) {
  const time = `${pad(eta.getHours())}:${pad(eta.getMinutes())}`
  if (sameDay(eta, now)) return `сегодня в ${time}`
  const tomorrow = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1)
  if (sameDay(eta, tomorrow)) return `завтра в ${time}`
  const date = `${pad(eta.getDate())}.${pad(eta.getMonth() + 1)}`
  return `${eta.getFullYear() === now.getFullYear() ? date : `${date}.${eta.getFullYear()}`} в ${time}`
}
