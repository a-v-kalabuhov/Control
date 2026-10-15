// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ShiftTimeline from '../ShiftTimeline.vue'

const H = 3_600_000
const now = Date.now()
const S = new Date(now - 6 * H)
const E = new Date(now + 6 * H)
const iso = (h) => new Date(now + h * H).toISOString()

const mountTimeline = (tasks) => mount(ShiftTimeline, {
  props: { statusSegments: [], tasks, shiftStart: S, shiftEnd: E }
})

// В наладке опоздал на 30 мин, наладка 30 мин против норматива 1ч, сейчас в выпуске.
const inRelease = {
  id: 'a', moldName: 'ПФ-12', planQuantity: 500, actualQuantity: 120,
  plannedStartAt: iso(-4), plannedSetupMinutes: 60,
  setupStartedAt: iso(-3.5), startedAt: iso(-3)
}
// Запланировано на эту смену, наладка не началась.
const notStarted = {
  id: 'b', moldName: 'ПФ-7', planQuantity: 300, actualQuantity: 0,
  plannedStartAt: iso(2), plannedSetupMinutes: 45
}

describe('ShiftTimeline — ряд «План» (BL-40)', () => {
  it('плановые бары с метками; неначатое задание — только в ряду «План»', () => {
    const w = mountTimeline([inRelease, notStarted])
    const plans = w.findAll('.plan-block')
    expect(plans).toHaveLength(2)
    expect(plans[0].text()).toContain('ПФ-12')
    expect(plans[0].find('.mark--late').text()).toContain('опозд. 30м')
    expect(plans[0].text()).toContain('наладка 30м / норм 1ч')
    expect(plans[1].text()).toContain('норм 45м')
    expect(w.findAll('.task-block')).toHaveLength(1)
  })

  it('тултип планового бара — плановое окно и отклонение', () => {
    const w = mountTimeline([inRelease])
    const title = w.find('.plan-block').attributes('title')
    expect(title).toMatch(/План наладки: \d\d:\d\d — \d\d:\d\d \(норм 1ч\)/)
    expect(title).toContain('Начало наладки: опозд. 30м')
  })

  it('тултип бара факта дополнен планом наладки', () => {
    const w = mountTimeline([inRelease])
    expect(w.find('.task-block').attributes('title')).toContain('План наладки:')
  })

  it('перекрывающиеся задания — разные дорожки', () => {
    const overlapping = {
      id: 'c', moldName: 'ПФ-3', planQuantity: 100, actualQuantity: 0,
      plannedStartAt: iso(-2), plannedSetupMinutes: 30,
      setupStartedAt: iso(-2), startedAt: iso(-1.5)
    }
    const w = mountTimeline([inRelease, overlapping])
    const tops = w.findAll('.task-block').map(b => b.attributes('style').match(/top:\s*([\d.]+px)/)[1])
    expect(new Set(tops).size).toBe(2)
  })

  it('план вне смены — нет бара', () => {
    const w = mountTimeline([{ ...notStarted, plannedStartAt: iso(-10) }])
    expect(w.findAll('.plan-block')).toHaveLength(0)
  })
})

describe('ShiftTimeline — клик по заданию', () => {
  it('клик по бару задания (факт и план) сообщает выбранное задание', async () => {
    const w = mountTimeline([inRelease, notStarted])

    await w.find('.task-block').trigger('click')
    await w.findAll('.plan-block')[1].trigger('click')

    expect(w.emitted('task-click')).toEqual([[inRelease], [notStarted]])
  })
})
