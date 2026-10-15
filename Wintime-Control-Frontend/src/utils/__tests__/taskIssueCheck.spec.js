import { describe, it, expect, vi } from 'vitest'
import { archivedTaskRefs } from '../taskIssueCheck'

vi.mock('@/api/imm', () => ({ immApi: { getById: vi.fn() } }))
vi.mock('@/api/molds', () => ({ moldsApi: { getById: vi.fn() } }))
import { immApi } from '@/api/imm'
import { moldsApi } from '@/api/molds'

const task = { immId: 'i1', moldId: 'm1', immName: 'ТПА-1', moldName: 'ПФ-1' }

describe('archivedTaskRefs', () => {
  it('всё активно — пусто', async () => {
    immApi.getById.mockResolvedValue({ data: { isActive: true } })
    moldsApi.getById.mockResolvedValue({ data: { isActive: true } })
    expect(await archivedTaskRefs(task)).toEqual([])
  })

  it('архивные ТПА и ПФ — обе подписи', async () => {
    immApi.getById.mockResolvedValue({ data: { isActive: false } })
    moldsApi.getById.mockResolvedValue({ data: { isActive: false } })
    expect(await archivedTaskRefs(task)).toEqual(['ТПА «ТПА-1»', 'пресс-форма «ПФ-1»'])
  })

  it('ошибка запроса пробрасывается', async () => {
    immApi.getById.mockRejectedValue(new Error('net'))
    moldsApi.getById.mockResolvedValue({ data: { isActive: true } })
    await expect(archivedTaskRefs(task)).rejects.toThrow('net')
  })
})
