export function formatRunStatus(run) {
  if (run.assignedTaskId) {
    return `Назначено: ${run.assignedTaskLabel ?? ''}`.trim()
  }
  return 'Не назначено'
}
