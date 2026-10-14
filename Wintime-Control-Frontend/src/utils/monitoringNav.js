// Навигация «карточка ТПА ↔ мониторинг» через query URL (спека 2026-10-14-monitoring-page-design).
// /monitoring?imm=<id>&from=card — страница мониторинга; /?imm=<id> — дашборд с открытой карточкой.

export function monitoringQueryWithImm(query, immId) {
  const next = { ...query }
  if (immId) next.imm = immId
  else delete next.imm
  return next
}

export function showBackToCard(query) {
  return query.from === 'card' && !!query.imm
}

export function dashboardQueryWithoutImm(query) {
  const next = { ...query }
  delete next.imm
  return next
}

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

// API-маршруты — {id:guid}, а SPA-fallback отдаёт 200 с HTML, поэтому мусорный ?imm надо отсекать на клиенте.
export function isImmId(value) {
  return typeof value === 'string' && GUID_RE.test(value)
}
