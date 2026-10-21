import { describe, it, expect } from 'vitest'
import {
  periodFor, stepAnchor, gridTicks, spanPct, periodLabel,
  boardSegments, pairLayout, rowLanes, boardMarks, planLabel, tooltipLines
} from '../planningBoard'

const d = (y, m, day, h = 0, min = 0) => new Date(y, m - 1, day, h, min)

describe('periodFor', () => {
  it('сутки — от начала первой смены текущих суток', () => {
    const p = periodFor('day', d(2026, 10, 15, 10), 480)
    expect(p.from).toEqual(d(2026, 10, 15, 8))
    expect(p.to).toEqual(d(2026, 10, 16, 8))
  })

  it('сутки до начала первой смены — это ещё предыдущие сутки', () => {
    const p = periodFor('day', d(2026, 10, 15, 6), 480)
    expect(p.from).toEqual(d(2026, 10, 14, 8))
  })

  it('неделя — с понедельника 00:00', () => {
    expect(periodFor('week', d(2026, 10, 15, 10))).toEqual({ from: d(2026, 10, 12), to: d(2026, 10, 19) })
  })

  it('воскресенье относится к неделе с прошедшего понедельника', () => {
    expect(periodFor('week', d(2026, 10, 18, 23)).from).toEqual(d(2026, 10, 12))
  })

  it('месяц — календарный', () => {
    expect(periodFor('month', d(2026, 10, 15))).toEqual({ from: d(2026, 10, 1), to: d(2026, 11, 1) })
  })
})

describe('stepAnchor', () => {
  it('сутки ±1 день, неделя ±7 дней', () => {
    expect(stepAnchor('day', d(2026, 10, 15, 10), 1)).toEqual(d(2026, 10, 16, 10))
    expect(stepAnchor('week', d(2026, 10, 15, 10), -1)).toEqual(d(2026, 10, 8, 10))
  })

  it('месяц не перескакивает через короткий месяц', () => {
    expect(stepAnchor('month', d(2026, 1, 31), 1)).toEqual(d(2026, 2, 1))
  })
})

describe('gridTicks', () => {
  it('сутки — начала смен внутри окна', () => {
    const shifts = [{ startMinutes: 480 }, { startMinutes: 1200 }]
    const ticks = gridTicks('day', d(2026, 10, 15, 8), d(2026, 10, 16, 8), shifts)
    expect(ticks.map(t => t.pct)).toEqual([0, 50])
    expect(ticks[1].at).toEqual(d(2026, 10, 15, 20))
  })

  it('неделя — 7 границ суток', () => {
    const ticks = gridTicks('week', d(2026, 10, 12), d(2026, 10, 19))
    expect(ticks).toHaveLength(7)
    expect(ticks[1].pct).toBeCloseTo(100 / 7)
  })

  it('месяц — по числу дней, подпись — число', () => {
    const ticks = gridTicks('month', d(2026, 10, 1), d(2026, 11, 1))
    expect(ticks).toHaveLength(31)
    expect(ticks[14].label).toBe('15')
  })
})

describe('spanPct', () => {
  const from = d(2026, 10, 15, 0)
  const to = d(2026, 10, 15, 10)

  it('интервал внутри окна', () => {
    const s = spanPct(d(2026, 10, 15, 1), d(2026, 10, 15, 3), from, to)
    expect(s.leftPct).toBeCloseTo(10)
    expect(s.widthPct).toBeCloseTo(20)
  })

  it('обрезается краями окна', () => {
    const s = spanPct(d(2026, 10, 14, 20), d(2026, 10, 15, 2), from, to)
    expect(s.leftPct).toBeCloseTo(0)
    expect(s.widthPct).toBeCloseTo(20)
  })

  it('вне окна или нулевой длины — null', () => {
    expect(spanPct(d(2026, 10, 15, 11), d(2026, 10, 15, 12), from, to)).toBeNull()
    expect(spanPct(d(2026, 10, 15, 2), d(2026, 10, 15, 2), from, to)).toBeNull()
  })
})

describe('periodLabel', () => {
  it('неделя — даты первого и последнего дня', () => {
    expect(periodLabel('week', d(2026, 10, 12), d(2026, 10, 19))).toBe('12.10.2026 — 18.10.2026')
  })
})

const MIN = 60_000
const T0 = new Date(2026, 9, 15, 10, 0).getTime()
const at = (m) => new Date(T0 + m * MIN).toISOString()

// План: наладка 10:00–10:30, выпуск до 11:30, буфер до 11:40.
function boardItem({ task = {}, fact = {}, ...rest } = {}) {
  return {
    task: { id: 't1', status: 'Planned', moldName: 'ПФ-1', immName: 'ТПА-1', planQuantity: 100,
            plannedSetupMinutes: 30, plannedReleaseBufferSeconds: 600, startedAt: null, ...task },
    productArticle: 'ART-1',
    plan: { setupStart: at(0), setupEnd: at(30), releaseEnd: at(90), bufferEnd: at(100) },
    fact: { setupStart: null, setupEnd: null, releaseEnd: null, forecastEnd: null, ...fact },
    bufferRemainingSeconds: null, overdueNotStartedSeconds: null, setupLagSeconds: null,
    ...rest
  }
}
const kinds = (segs) => segs.map(s => s.kind)

describe('boardSegments', () => {
  it('запланированное — три сегмента плана, факта нет', () => {
    const s = boardSegments(boardItem(), new Date(T0 - 60 * MIN))
    expect(kinds(s.plan)).toEqual(['setup', 'release', 'buffer'])
    expect(s.fact).toEqual([])
  })

  it('нулевой норматив наладки — сегмент наладки не рисуется', () => {
    const item = boardItem()
    item.plan.setupEnd = item.plan.setupStart
    expect(kinds(boardSegments(item, new Date(T0)).plan)).toEqual(['release', 'buffer'])
  })

  it('выпуск идёт, прогноз в буфере — факт до «сейчас», прогноз, остаток', () => {
    const item = boardItem({
      task: { status: 'InProgress', startedAt: at(32) },
      fact: { setupStart: at(5), setupEnd: at(32), forecastEnd: at(95) },
      bufferRemainingSeconds: 300
    })
    const s = boardSegments(item, new Date(T0 + 60 * MIN))
    expect(kinds(s.fact)).toEqual(['setup', 'release', 'forecast', 'remaining'])
    const rest = s.fact.at(-1)
    expect([rest.start, rest.end]).toEqual([T0 + 95 * MIN, T0 + 100 * MIN])
  })

  it('прогноз за плановым окончанием — перерасход от P до прогноза', () => {
    const item = boardItem({
      task: { status: 'InProgress', startedAt: at(32) },
      fact: { setupStart: at(5), setupEnd: at(32), forecastEnd: at(110) },
      bufferRemainingSeconds: -600
    })
    const over = boardSegments(item, new Date(T0 + 60 * MIN)).fact.at(-1)
    expect(over.kind).toBe('overrun')
    expect([over.start, over.end]).toEqual([T0 + 100 * MIN, T0 + 110 * MIN])
  })

  it('выполнено — наладка, выпуск до завершения, остаток', () => {
    const item = boardItem({
      task: { status: 'Completed', startedAt: at(30) },
      fact: { setupStart: at(0), setupEnd: at(30), releaseEnd: at(85) },
      bufferRemainingSeconds: 900
    })
    expect(kinds(boardSegments(item, new Date(T0 + 200 * MIN)).fact)).toEqual(['setup', 'release', 'remaining'])
  })

  it('идёт наладка — факт наладки до «сейчас»', () => {
    const item = boardItem({ task: { status: 'Setup' }, fact: { setupStart: at(10) } })
    const s = boardSegments(item, new Date(T0 + 50 * MIN))
    expect(s.fact).toEqual([{ kind: 'setup', start: T0 + 10 * MIN, end: T0 + 50 * MIN }])
  })
})

describe('pairLayout', () => {
  it('рамка пары — объединение сегментов, сегменты — в % рамки', () => {
    const l = pairLayout(boardItem(), new Date(T0), new Date(T0 + 200 * MIN), new Date(T0 - MIN))
    expect(l.box.leftPct).toBeCloseTo(0)
    expect(l.box.widthPct).toBeCloseTo(50)
    expect(l.plan.map(s => [s.kind, Math.round(s.leftPct), Math.round(s.widthPct)]))
      .toEqual([['setup', 0, 30], ['release', 30, 60], ['buffer', 90, 10]])
  })

  it('пара вне окна — null', () => {
    expect(pairLayout(boardItem(), new Date(T0 + 300 * MIN), new Date(T0 + 400 * MIN), new Date(T0))).toBeNull()
  })
})

describe('rowLanes', () => {
  it('пересекающиеся пары — разные дорожки, непересекающиеся — одна', () => {
    const a = boardItem()
    const b = boardItem({ task: { id: 't2' } })
    b.plan = { setupStart: at(50), setupEnd: at(60), releaseEnd: at(120), bufferEnd: at(130) }
    const c = boardItem({ task: { id: 't3' } })
    c.plan = { setupStart: at(200), setupEnd: at(210), releaseEnd: at(220), bufferEnd: at(230) }
    expect(rowLanes([a, b, c], new Date(T0))).toEqual([0, 1, 0])
  })
})

describe('boardMarks', () => {
  it('просрочка неначатого — на план-баре', () => {
    expect(boardMarks(boardItem({ overdueNotStartedSeconds: 7800 })))
      .toEqual([{ text: 'не начато, опоздание 2ч 10м', row: 'plan' }])
  })

  it('отставание наладки и перерасход — на факт-баре', () => {
    expect(boardMarks(boardItem({ setupLagSeconds: 1500 })))
      .toEqual([{ text: 'наладка отстала на 25м', row: 'fact' }])
    expect(boardMarks(boardItem({ bufferRemainingSeconds: -2400 })))
      .toEqual([{ text: 'перерасход 40м', row: 'fact' }])
  })

  it('меньше минуты — без меток', () => {
    expect(boardMarks(boardItem({ overdueNotStartedSeconds: 30, setupLagSeconds: 59, bufferRemainingSeconds: -10 })))
      .toEqual([])
  })
})

describe('подписи', () => {
  it('план-бар: артикул · ПФ · план', () => {
    expect(planLabel(boardItem())).toBe('ART-1 · ПФ-1 · 100 шт')
    expect(planLabel(boardItem({ productArticle: null }))).toBe('ПФ-1 · 100 шт')
  })

  it('тултип: статус, перерасход', () => {
    const lines = tooltipLines(boardItem({ bufferRemainingSeconds: -2400 }))
    expect(lines).toContain('Статус: Планируется')
    expect(lines).toContain('Перерасход: 40м')
  })
})

describe('PZP-07b: прогноз Issued и строки конфликтов', () => {
  const MIN = 60_000
  const T0 = new Date(2026, 9, 15, 10, 0).getTime()
  const at = (m) => new Date(T0 + m * MIN).toISOString()
  const issued = {
    task: { id: 't1', status: 'Issued', moldName: 'ПФ-1', immName: 'ТПА-1', planQuantity: 100, plannedSetupMinutes: 30 },
    productArticle: 'A1.0',
    plan: { setupStart: at(0), setupEnd: at(30), releaseEnd: at(90), bufferEnd: at(100) },
    fact: { setupStart: null, setupEnd: null, releaseEnd: null, forecastEnd: at(120), forecastStart: at(30) },
    bufferRemainingSeconds: null, overdueNotStartedSeconds: 1200, setupLagSeconds: null,
    conflicts: [{ kind: 'Deviation', resource: 'Imm', overlapSeconds: 2400,
      task: { id: 't0', productArticle: 'A0.0', moldName: 'ПФ-0', immName: 'ТПА-1' },
      other: { id: 't1', productArticle: 'A1.0', moldName: 'ПФ-1', immName: 'ТПА-1' } }]
  }

  it('прогноз Issued начинается с forecastStart, а не с «сейчас»', () => {
    const { fact } = boardSegments(issued, new Date(T0 + 20 * MIN))
    expect(fact).toEqual([{ kind: 'forecast', start: T0 + 30 * MIN, end: T0 + 120 * MIN }])
  })

  it('тултип содержит строку конфликта', () => {
    expect(tooltipLines(issued)).toContain('Конфликт отклонения по ТПА с «A0.0 · ПФ-0 · ТПА-1», пересечение 40м')
  })
})
