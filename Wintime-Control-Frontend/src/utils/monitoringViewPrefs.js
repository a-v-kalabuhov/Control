// Вид страницы «Мониторинг» (выбранные датчики и порядок диаграмм) в localStorage —
// на пару (пользователь, шаблон ТПА). Спека 2026-10-14-monitoring-view-prefs-design.
// Любая ошибка хранилища или мусор в значении → «настроек нет», исключения наружу не выходят.

const PREFIX = 'wtctrl.monitoring.view.v1'

export function viewPrefsKey(userId, templateId) {
  if (!userId || !templateId) return null
  return `${PREFIX}.${userId}.${templateId}`
}

export function browserStorage() {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

export function readViewPrefs(storage, key) {
  if (!storage || !key) return null
  try {
    const raw = storage.getItem(key)
    if (raw == null) return null
    const value = JSON.parse(raw)
    return Array.isArray(value) && value.every(v => typeof v === 'string') ? value : null
  } catch {
    return null
  }
}

export function writeViewPrefs(storage, key, order) {
  if (!storage || !key) return
  try {
    storage.setItem(key, JSON.stringify(order))
  } catch {
    // приватный режим / квота — вид просто не запомнится
  }
}

// Сохранённый порядок, ограниченный датчиками ТПА; иначе — первый датчик (как без настроек).
export function resolveSignalOrder(available, saved) {
  const avail = new Set(available)
  const order = [...new Set((saved ?? []).filter(n => avail.has(n)))]
  if (order.length) return order
  return available.length ? [available[0]] : []
}
