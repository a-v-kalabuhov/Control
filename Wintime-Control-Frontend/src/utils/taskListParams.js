// Фильтры списка заданий → query-параметры GET /api/tasks (ARCH-03).
// Даты из date-picker — 'YYYY-MM-DD' в поясе браузера.
// «Выдано» — включительно [начало from; конец to]; «В работе» — полуинтервал
// [начало from; начало дня после to); «Плановая дата» — дата завода, передаётся как есть.

function dayStart(ymd) {
  const [y, m, d] = ymd.split('-').map(Number)
  return new Date(y, m - 1, d)
}

function addDays(date, days) {
  const r = new Date(date)
  r.setDate(r.getDate() + days)
  return r
}

export function buildTaskListParams(filters) {
  const f = filters ?? {}
  const params = {
    status: f.status,
    immId: f.immId,
    personnelId: f.personnelId,
    issuedFrom: f.issuedFrom ? dayStart(f.issuedFrom).toISOString() : null,
    issuedTo: f.issuedTo ? new Date(addDays(dayStart(f.issuedTo), 1) - 1).toISOString() : null,
    plannedFrom: f.plannedFrom,
    plannedTo: f.plannedTo,
    activeFrom: f.activeFrom ? dayStart(f.activeFrom).toISOString() : null,
    activeTo: f.activeTo ? addDays(dayStart(f.activeTo), 1).toISOString() : null
  }
  Object.keys(params).forEach(key => {
    if (params[key] === null || params[key] === undefined || params[key] === '') delete params[key]
  })
  return params
}
