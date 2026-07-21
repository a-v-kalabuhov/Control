import { describe, it, expect, vi, beforeEach } from 'vitest'

vi.mock('@/api/imm', () => ({ immApi: { getById: vi.fn() } }))
vi.mock('@/api/molds', () => ({ moldsApi: { getById: vi.fn() } }))

import { describeScan, resolveScan } from '../scanDescription'
import { immApi } from '@/api/imm'
import { moldsApi } from '@/api/molds'

describe('resolveScan (структурный)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('ТПА → ok, тип и наименование', async () => {
    immApi.getById.mockResolvedValue({ data: { name: 'ТПА №1' } })
    expect(await resolveScan('{"entity":"machine","id":"g1"}')).toEqual({
      ok: true, typeLabel: 'ТПА', details: 'ТПА №1', text: 'ТПА: ТПА №1'
    })
  })

  it('Пресс-форма → ok, тип и «Наименование / Артикул»', async () => {
    moldsApi.getById.mockResolvedValue({ data: { name: 'КлипДак', formId: 'PF-001' } })
    expect(await resolveScan('{"entity":"mold","id":"m1"}')).toEqual({
      ok: true, typeLabel: 'Пресс-форма', details: 'КлипДак / PF-001', text: 'Пресс-форма: КлипДак / PF-001'
    })
  })

  it('битый JSON → ok:false', async () => {
    const r = await resolveScan('не json')
    expect(r.ok).toBe(false)
    expect(r.text).toBe('Неверный формат QR-кода')
  })

  it('сущность не найдена → ok:false', async () => {
    immApi.getById.mockRejectedValue(new Error('404'))
    const r = await resolveScan('{"entity":"machine","id":"x"}')
    expect(r.ok).toBe(false)
    expect(r.text).toBe('Объект не найден в системе')
  })
})

describe('describeScan', () => {
  beforeEach(() => vi.clearAllMocks())

  it('ТПА → «ТПА: Наименование»', async () => {
    immApi.getById.mockResolvedValue({ data: { name: 'ТПА №1' } })
    expect(await describeScan('{"entity":"machine","id":"g1"}')).toBe('ТПА: ТПА №1')
    expect(immApi.getById).toHaveBeenCalledWith('g1')
  })

  it('Пресс-форма → «Пресс-форма: Наименование / Артикул»', async () => {
    moldsApi.getById.mockResolvedValue({ data: { name: 'КлипДак', formId: 'PF-001' } })
    expect(await describeScan('{"entity":"mold","id":"m1"}')).toBe('Пресс-форма: КлипДак / PF-001')
    expect(moldsApi.getById).toHaveBeenCalledWith('m1')
  })

  it('битый JSON → сообщение о формате', async () => {
    expect(await describeScan('не json')).toBe('Неверный формат QR-кода')
  })

  it('сущность не найдена (ошибка API) → понятный текст', async () => {
    immApi.getById.mockRejectedValue(new Error('404'))
    expect(await describeScan('{"entity":"machine","id":"x"}')).toBe('Объект не найден в системе')
  })

  it('неизвестный тип entity → запасной текст с id', async () => {
    expect(await describeScan('{"entity":"foo","id":"z"}')).toBe('Объект: z')
  })
})
