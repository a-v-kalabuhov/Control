import { describe, it, expect } from 'vitest'
import { monitoringQueryWithImm, showBackToCard, dashboardQueryWithoutImm } from '@/utils/monitoringNav'

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
