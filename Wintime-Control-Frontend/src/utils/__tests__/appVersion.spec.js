import { describe, it, expect } from 'vitest'
import { formatAppVersion } from '../appVersion'

describe('formatAppVersion', () => {
  it('сборка: номер и дата ДД.ММ.ГГГГ', () => {
    // полдень UTC — одна и та же календарная дата в любом поясе от -11 до +11
    const r = formatAppVersion({ version: '0.9.0', buildDate: '2026-09-29T12:00:00Z', commit: 'abc1234' })
    expect(r.label).toBe('v0.9.0 · 29.09.2026')
    expect(r.tooltip).toBe('commit abc1234')
  })

  it('dev-режим: пустая дата → «dev»', () => {
    const r = formatAppVersion({ version: '0.9.0', buildDate: '', commit: 'abc1234' })
    expect(r.label).toBe('v0.9.0 · dev')
  })

  it('без git: пустой хэш → пустая подсказка', () => {
    const r = formatAppVersion({ version: '0.9.0', buildDate: '2026-01-05T12:00:00Z', commit: '' })
    expect(r.label).toBe('v0.9.0 · 05.01.2026')
    expect(r.tooltip).toBe('')
  })
})
