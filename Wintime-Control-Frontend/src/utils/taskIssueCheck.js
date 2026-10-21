import { immApi } from '@/api/imm'
import { moldsApi } from '@/api/molds'

// Перед выдачей: ТПА и пресс-форма задания не ушли в архив. Возвращает подписи архивных
// ссылок (пусто — можно выдавать); ошибку запроса пробрасывает вызывающему.
export async function archivedTaskRefs(task) {
  const [immRes, moldRes] = await Promise.all([immApi.getById(task.immId), moldsApi.getById(task.moldId)])
  const refs = []
  if (!immRes.data.isActive) refs.push(`ТПА «${task.immName}»`)
  if (!moldRes.data.isActive) refs.push(`пресс-форма «${task.moldName}»`)
  return refs
}
