import { describe, it, expect } from 'vitest'
import { monitoringQueryWithImm, showBackToCard, dashboardQueryWithoutImm, isImmId } from '@/utils/monitoringNav'

describe('monitoringQueryWithImm', () => {
  it('ставит imm и сохраняет прочие ключи', () => {
    expect(monitoringQueryWithImm({ from: 'card', imm: 'a' }, 'b')).toEqual({ from: 'card', imm: 'b' })
  })

  it('удаляет imm при пустом значении', () => {
    expect(monitoringQueryWithImm({ from: 'card', imm: 'a' }, '')).toEqual({ from: 'card' })
    expect(monitoringQueryWithImm({ imm: 'a' }, null)).toEqual({})
    expect(monitoringQueryWithImm({ imm: 'a' }, undefined)).toEqual({})
  })

  it('не мутирует исходный query', () => {
    const q = { imm: 'a' }
    monitoringQueryWithImm(q, 'b')
    expect(q).toEqual({ imm: 'a' })
  })
})

describe('showBackToCard', () => {
  it('true при from=card и выбранном ТПА', () => {
    expect(showBackToCard({ from: 'card', imm: 'a' })).toBe(true)
  })

  it('false без from=card', () => {
    expect(showBackToCard({ imm: 'a' })).toBe(false)
    expect(showBackToCard({ from: 'menu', imm: 'a' })).toBe(false)
  })

  it('false без ТПА', () => {
    expect(showBackToCard({ from: 'card' })).toBe(false)
    expect(showBackToCard({ from: 'card', imm: '' })).toBe(false)
  })
})

describe('dashboardQueryWithoutImm', () => {
  it('убирает imm и сохраняет прочие ключи', () => {
    expect(dashboardQueryWithoutImm({ imm: 'a', x: '1' })).toEqual({ x: '1' })
  })

  it('не мутирует исходный query', () => {
    const q = { imm: 'a' }
    dashboardQueryWithoutImm(q)
    expect(q).toEqual({ imm: 'a' })
  })
})

describe('isImmId', () => {
  it('true для GUID в нижнем и верхнем регистре', () => {
    expect(isImmId('3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(true)
    expect(isImmId('3F2504E0-4F89-11D3-9A0C-0305E82C3301')).toBe(true)
  })

  it('false для не-GUID', () => {
    expect(isImmId('foo')).toBe(false)
    expect(isImmId('')).toBe(false)
    expect(isImmId(undefined)).toBe(false)
    expect(isImmId(null)).toBe(false)
    expect(isImmId(['3f2504e0-4f89-11d3-9a0c-0305e82c3301'])).toBe(false)
    expect(isImmId('3f2504e0-4f89-11d3-9a0c-0305e82c3301x')).toBe(false)
  })
})
