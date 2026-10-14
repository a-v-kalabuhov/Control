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
