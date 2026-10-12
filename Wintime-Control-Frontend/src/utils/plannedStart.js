// PZP-13: плановое начало задания.
// plannedDate ('YYYY-MM-DD') и plannedSetupStartTime ('HH:mm:ss') показываются как ввёл
// менеджер — по часам завода. plannedStartAt (UTC, вычислен сервером) — для сравнения с «сейчас».

const NOT_STARTED = new Set(['Draft', 'Issued'])

export function formatPlannedStart(task) {
  if (!task?.plannedDate) return '—'
  const [, month, day] = task.plannedDate.split('-')
  const date = `${day}.${month}`
  return task.plannedSetupStartTime ? `${date} ${task.plannedSetupStartTime.slice(0, 5)}` : date
}

export function isPlannedStartOverdue(task, now = new Date()) {
  if (!NOT_STARTED.has(task?.status) || !task.plannedStartAt) return false
  return now.getTime() > new Date(task.plannedStartAt).getTime()
}

export function isPlannedStartToday(task, now = new Date()) {
  if (!task?.plannedStartAt) return false
  const start = new Date(task.plannedStartAt)
  return start.getFullYear() === now.getFullYear()
    && start.getMonth() === now.getMonth()
    && start.getDate() === now.getDate()
}
