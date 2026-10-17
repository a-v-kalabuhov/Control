import { describe, it, expect } from 'vitest'
import dayjs from 'dayjs'
import { buildStatusRows, summarizeStatusRows, isCutAtNow, formatDuration, formatPoint } from '../statusRows'

// Локальное время браузера — как в отчёте.
const t = (s) => dayjs(`2026-10-01 ${s}`).valueOf()
const iso = (s) => dayjs(`2026-10-01 ${s}`).toISOString()
const seg = (type, from, to) => ({ type, start: iso(from), end: iso(to) })

describe('buildStatusRows', () => {
  it('обрезает сегменты по границам периода (поля графика ±30 мин отбрасываются)', () => {
    const rows = buildStatusRows(
      [seg('NoTask', '07:30', '08:10'), seg('Production', '08:10', '16:00'), seg('NoTask', '16:00', '16:30')],
      iso('08:00'), iso('16:00'))
    expect(rows).toEqual([
      { type: 'NoTask', start: t('08:00'), end: t('08:10'), durationSeconds: 600 },
      { type: 'Production', start: t('08:10'), end: t('16:00'), durationSeconds: 28200 }
    ])
  })

  it('склеивает соседние сегменты одного типа без разрыва', () => {
    const rows = buildStatusRows(
      [seg('Production', '09:00', '10:00'), seg('Production', '08:00', '09:00'), seg('Setup', '10:00', '10:05')],
      iso('08:00'), iso('16:00'))
    expect(rows.map(r => [r.type, r.durationSeconds])).toEqual([['Production', 7200], ['Setup', 300]])
  })

  it('не склеивает одинаковые сегменты с разрывом', () => {
    const rows = buildStatusRows(
      [seg('Production', '08:00', '09:00'), seg('Production', '09:01', '10:00')],
      iso('08:00'), iso('16:00'))
    expect(rows).toHaveLength(2)
  })

  it('ночная смена через полночь', () => {
    const next = (s) => dayjs(`2026-10-02 ${s}`).toISOString()
    const rows = buildStatusRows(
      [{ type: 'Production', start: iso('23:00'), end: next('01:00') }],
      iso('20:00'), next('08:00'))
    expect(rows[0].durationSeconds).toBe(7200)
  })

  it('отбрасывает сегменты нулевой длины и вне периода; пустой timeline → []', () => {
    expect(buildStatusRows([seg('Setup', '08:00', '08:00'), seg('Setup', '06:00', '07:00')], iso('08:00'), iso('16:00'))).toEqual([])
    expect(buildStatusRows(undefined, iso('08:00'), iso('16:00'))).toEqual([])
  })
})

describe('summarizeStatusRows', () => {
  it('суммы по типам в порядке палитры, только ненулевые; неизвестный тип → NoData', () => {
    const rows = [
      { type: 'Setup', durationSeconds: 300 },
      { type: 'Production', durationSeconds: 3600 },
      { type: 'Production', durationSeconds: 60 },
      { type: 'Weird', durationSeconds: 5 }
    ]
    expect(summarizeStatusRows(rows)).toEqual([
      { type: 'Production', seconds: 3660 },
      { type: 'Setup', seconds: 300 },
      { type: 'NoData', seconds: 5 }
    ])
  })
})

describe('isCutAtNow', () => {
  it('последний сегмент кончается раньше конца периода, а период ещё идёт → true', () => {
    const rows = [{ end: t('10:00') }]
    expect(isCutAtNow(rows, iso('16:00'), t('10:00'))).toBe(true)
  })
  it('прошедший период или сегмент до конца периода → false', () => {
    expect(isCutAtNow([{ end: t('16:00') }], iso('16:00'), t('10:00'))).toBe(false)
    expect(isCutAtNow([{ end: t('10:00') }], iso('16:00'), t('17:00'))).toBe(false)
    expect(isCutAtNow([], iso('16:00'), t('10:00'))).toBe(false)
  })
})

describe('formatDuration', () => {
  it.each([[0, '0:00:00'], [59, '0:00:59'], [247, '0:04:07'], [3600, '1:00:00'], [97200, '27:00:00']])(
    '%i с → %s', (s, expected) => expect(formatDuration(s)).toBe(expected))
})

describe('formatPoint', () => {
  it('та же дата, что у начала периода → только время', () => {
    expect(formatPoint(t('08:05:09'), iso('08:00'))).toBe('08:05:09')
  })
  it('другая дата → с датой', () => {
    expect(formatPoint(dayjs('2026-10-02 01:00:00').valueOf(), iso('20:00'))).toBe('02.10 01:00:00')
  })
})
