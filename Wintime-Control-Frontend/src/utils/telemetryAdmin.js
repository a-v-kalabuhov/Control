import dayjs from 'dayjs'
import 'dayjs/locale/ru'

const units = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ']
const number = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 })

export function formatBytes(bytes) {
  let value = Number(bytes)
  let unit = 0
  while (Math.abs(value) >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${number.format(value)} ${units[unit]}`
}

export function formatDateRange(period) {
  return `${dayjs(period.startDate).format('DD.MM.YYYY')} — ${dayjs(period.endDate).format('DD.MM.YYYY')}`
}

export function formatMonth(period) {
  return dayjs(period.startDate).locale('ru').format('MMMM YYYY')
}
