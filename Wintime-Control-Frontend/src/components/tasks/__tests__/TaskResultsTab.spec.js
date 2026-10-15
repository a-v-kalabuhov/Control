// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'

vi.mock('@/api/tasks', () => ({
  tasksApi: { getResults: vi.fn() }
}))

import { tasksApi } from '@/api/tasks'
import TaskResultsTab from '../TaskResultsTab.vue'

const completed = () => ({
  releaseStarted: true,
  releaseFrom: '2026-10-14T13:09:00Z',
  releaseTo: '2026-10-15T16:41:00Z',
  inProgress: false,
  output: {
    planQuantity: 100, actualQuantity: 110, defectQuantity: 4,
    goodQuantity: 106, overrunQuantity: 10, deviationReason: 'Перевыпуск по просьбе мастера'
  },
  cycles: {
    count: 567, avgInjectionSeconds: 12.84, plannedInjectionSeconds: 55,
    avgFullCycleSeconds: 66, plannedFullCycleSeconds: 60
  },
  time: { windowSeconds: 3600, productionSeconds: 1800, downtimeSeconds: 1200, downtimeCount: 4, offlineSeconds: 600 }
})

function mountTab() {
  return mount(TaskResultsTab, {
    props: { taskId: 't1' },
    global: { plugins: [ElementPlus] }
  })
}

const rowText = (w, testId) => w.find(`[data-test="${testId}"]`).text()

describe('TaskResultsTab', () => {
  beforeEach(() => { vi.clearAllMocks() })
  afterEach(() => { vi.useRealTimers() })

  it('выпуск не начат — одна строка, без блоков', async () => {
    tasksApi.getResults.mockResolvedValue({ data: { releaseStarted: false, inProgress: false } })
    const w = mountTab()
    await flushPromises()

    expect(tasksApi.getResults).toHaveBeenCalledWith('t1')
    expect(w.text()).toContain('Выпуск ещё не начат')
    expect(w.find('[data-test="output"]').exists()).toBe(false)
  })

  it('выпуск: план, выпущено, брак, годных, перевыпуск, причина', async () => {
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    const w = mountTab()
    await flushPromises()

    const out = rowText(w, 'output')
    expect(out).toContain('100')
    expect(out).toContain('110')
    expect(out).toContain('106')
    expect(out).toContain('10')
    expect(out).toContain('Перевыпуск по просьбе мастера')
  })

  it('без перевыпуска и причины — «—» и строки причины нет', async () => {
    const data = completed()
    data.output.overrunQuantity = 0
    data.output.deviationReason = null
    tasksApi.getResults.mockResolvedValue({ data })
    const w = mountTab()
    await flushPromises()

    expect(w.find('[data-test="overrun"]').text()).toBe('—')
    expect(w.find('[data-test="deviation-reason"]').exists()).toBe(false)
  })

  it('циклы: факт, эталон и отклонение со знаком и цветом', async () => {
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    const w = mountTab()
    await flushPromises()

    const inj = rowText(w, 'cycle-injection')
    expect(inj).toContain('12.8 с')
    expect(inj).toContain('55')
    expect(inj).toContain('−77 %')
    expect(w.find('[data-test="cycle-injection"] [data-test="deviation"]').classes()).toContain('text-green-600')

    const full = rowText(w, 'cycle-full')
    expect(full).toContain('66.0 с')
    expect(full).toContain('+10 %')
    expect(w.find('[data-test="cycle-full"] [data-test="deviation"]').classes()).toContain('text-red-600')
    expect(rowText(w, 'cycles-header')).toContain('567')
  })

  it('нет среднего или эталона — «—»', async () => {
    const data = completed()
    data.cycles.avgInjectionSeconds = null
    data.cycles.plannedFullCycleSeconds = null
    tasksApi.getResults.mockResolvedValue({ data })
    const w = mountTab()
    await flushPromises()

    expect(w.find('[data-test="cycle-injection"] [data-test="deviation"]').text()).toBe('—')
    expect(w.find('[data-test="cycle-full"] [data-test="deviation"]').text()).toBe('—')
  })

  it('время выпуска: длительности, проценты, количество простоев', async () => {
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    const w = mountTab()
    await flushPromises()

    expect(rowText(w, 'time-Production')).toContain('30 мин')
    expect(rowText(w, 'time-Production')).toContain('50 %')
    expect(rowText(w, 'time-Downtime')).toContain('Простои (4)')
    expect(rowText(w, 'time-Downtime')).toContain('33 %')
    expect(rowText(w, 'time-Offline')).toContain('Нет связи')
    expect(rowText(w, 'time-Offline')).toContain('17 %')
  })

  it('задание в работе — пометка «в работе» и обновление раз в минуту', async () => {
    // Только таймеры компонента: flushPromises из test-utils ходит через setImmediate — его не подменяем
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    tasksApi.getResults.mockResolvedValue({ data: { ...completed(), inProgress: true } })
    const w = mountTab()
    await flushPromises()

    expect(w.text()).toContain('в работе')
    expect(tasksApi.getResults).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(60_000)
    expect(tasksApi.getResults).toHaveBeenCalledTimes(2)

    w.unmount()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(tasksApi.getResults).toHaveBeenCalledTimes(2)   // таймер снят
  })

  it('завершённое задание не опрашивается повторно', async () => {
    // Только таймеры компонента: flushPromises из test-utils ходит через setImmediate — его не подменяем
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    tasksApi.getResults.mockResolvedValue({ data: completed() })
    mountTab()
    await flushPromises()

    await vi.advanceTimersByTimeAsync(120_000)
    expect(tasksApi.getResults).toHaveBeenCalledTimes(1)
  })
})
