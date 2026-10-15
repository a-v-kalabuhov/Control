// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal()),
  useRoute: () => ({ query: { imm: '11111111-1111-1111-1111-111111111111' } }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() })
}))
vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn(), getById: vi.fn() } }))
vi.mock('@/api/telemetry', () => ({ telemetryApi: { getSignals: vi.fn(), getDashboard: vi.fn() } }))

const { immApi } = await import('@/api/imm')
const { telemetryApi } = await import('@/api/telemetry')
const { default: TelemetryDashboardView } = await import('../TelemetryDashboardView.vue')

const IMM = '11111111-1111-1111-1111-111111111111'
const F = 'YYYY-MM-DDTHH:mm:ss'
const dashboard = (truncated = false) => ({ data: { signals: [], cycles: [], statusSegments: [], truncated } })
const iso = (local) => new Date(local).toISOString()

// Страница в режиме «История» с заданными полями периода; вызовы API до этого момента сброшены.
async function mountHistory(from, to) {
  const w = mount(TelemetryDashboardView, {
    global: { plugins: [ElementPlus], stubs: { SignalChart: true, 'el-date-picker': true, ArrowLeft: true } }
  })
  await flushPromises()
  w.vm.mode = 'history'
  w.vm.onModeChange()
  w.vm.historyFrom = from
  w.vm.historyTo = to
  await flushPromises()
  telemetryApi.getDashboard.mockClear()
  return w
}

describe('TelemetryDashboardView — сдвиг окна истории', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    immApi.getList.mockResolvedValue({ data: [{ id: IMM, name: 'ТПА-4', templateId: 't1' }] })
    telemetryApi.getSignals.mockResolvedValue({ data: [{ parameterName: 'p1', name: 'p1', type: 'number' }] })
    telemetryApi.getDashboard.mockResolvedValue(dashboard())
  })

  it('◀ сдвигает обе границы на треть окна и загружает новое окно', async () => {
    const w = await mountHistory('2026-10-01T10:00:00', '2026-10-01T11:00:00')

    await w.find('[data-test="shift-back"]').trigger('click')
    await flushPromises()

    expect(w.vm.historyFrom).toBe('2026-10-01T09:40:00')
    expect(w.vm.historyTo).toBe('2026-10-01T10:40:00')
    expect(telemetryApi.getDashboard).toHaveBeenCalledTimes(1)
    const params = telemetryApi.getDashboard.mock.calls[0][1]
    expect(params.from).toBe(iso('2026-10-01T09:40:00'))
    expect(params.to).toBe(iso('2026-10-01T10:40:00'))
  })

  it('▶ сдвигает прошлое окно вправо на треть', async () => {
    const w = await mountHistory('2026-10-01T10:00:00', '2026-10-01T11:00:00')

    await w.find('[data-test="shift-forward"]').trigger('click')
    await flushPromises()

    expect(w.vm.historyFrom).toBe('2026-10-01T10:20:00')
    expect(w.vm.historyTo).toBe('2026-10-01T11:20:00')
  })

  it('▶ клампит конец к «сейчас» с точностью до секунды и после этого неактивна', async () => {
    const to = dayjs().subtract(10, 'minute')
    const w = await mountHistory(to.subtract(1, 'hour').format(F), to.format(F))

    await w.find('[data-test="shift-forward"]').trigger('click')
    const nowAfterClick = dayjs().format(F)
    await flushPromises()

    const diff = Math.abs(new Date(w.vm.historyTo).getTime() - new Date(nowAfterClick).getTime())
    expect(diff).toBeLessThanOrEqual(1000)
    expect(new Date(w.vm.historyTo) - new Date(w.vm.historyFrom)).toBe(3600_000)
    expect(w.find('[data-test="shift-forward"]').attributes('disabled')).toBeDefined()
  })

  it('▶ неактивна, когда конец окна в «сейчас» или позже; ◀ при этом активна', async () => {
    const to = dayjs().add(10, 'minute')
    const w = await mountHistory(to.subtract(1, 'hour').format(F), to.format(F))

    expect(w.find('[data-test="shift-forward"]').attributes('disabled')).toBeDefined()
    expect(w.find('[data-test="shift-back"]').attributes('disabled')).toBeUndefined()
  })

  it('без границ периода обе кнопки неактивны', async () => {
    const w = await mountHistory(null, null)

    expect(w.find('[data-test="shift-back"]').attributes('disabled')).toBeDefined()
    expect(w.find('[data-test="shift-forward"]').attributes('disabled')).toBeDefined()
  })

  it('при двух быстрых сдвигах показывается ответ на последний, даже если первый пришёл позже', async () => {
    const w = await mountHistory('2026-10-01T10:00:00', '2026-10-01T11:00:00')
    let resolveFirst
    let resolveSecond
    telemetryApi.getDashboard
      .mockImplementationOnce(() => new Promise(r => { resolveFirst = r }))
      .mockImplementationOnce(() => new Promise(r => { resolveSecond = r }))

    await w.find('[data-test="shift-back"]').trigger('click')
    await w.find('[data-test="shift-back"]').trigger('click')
    resolveSecond(dashboard(false))
    await flushPromises()
    resolveFirst(dashboard(true))   // устаревший ответ: «Показаны не все точки»
    await flushPromises()

    expect(w.text()).not.toContain('Показаны не все точки')
  })
})
