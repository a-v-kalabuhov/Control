// Конфликты плана (PZP-07b): подписи, цвета, строки для баннера, тултипа и карточки.
import { formatDuration } from './shiftTimeline'

export const CONFLICT_KIND_LABELS = { Plan: 'Конфликт плана', Deviation: 'Конфликт отклонения' }
export const CONFLICT_KIND_COLORS = { Plan: '#f56c6c', Deviation: '#e6a23c' }

export const conflictsOf = (items, taskId) =>
  (items ?? []).filter(c => c.task.id === taskId || c.other.id === taskId)

// Конфликт плана важнее отклонения
export function worstKind(conflicts) {
  if (!conflicts?.length) return null
  return conflicts.some(c => c.kind === 'Plan') ? 'Plan' : 'Deviation'
}

export const otherSide = (conflict, taskId) => (conflict.task.id === taskId ? conflict.other : conflict.task)

export const taskRefLabel = (ref) => [ref.productArticle, ref.moldName, ref.immName].filter(Boolean).join(' · ')

function resourceText(conflict) {
  return conflict.resource === 'Imm' ? 'по ТПА' : `по ПФ «${conflict.task.moldName}»`
}

export function conflictLine(conflict, taskId) {
  const label = CONFLICT_KIND_LABELS[conflict.kind] ?? conflict.kind
  return `${label} ${resourceText(conflict)} с «${taskRefLabel(otherSide(conflict, taskId))}», ` +
    `пересечение ${formatDuration(conflict.overlapSeconds * 1000)}`
}
