// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal()),
  useRouter: () => ({ push: vi.fn() })
}))
vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/shifts', () => ({ shiftsApi: { getShifts: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/reports', () => ({ reportsApi: { getDaily: vi.fn() } }))

const { reportsApi } = await import('@/api/reports')
const { default: DailyReportView } = await import('../DailyReportView.vue')

const H = 3600
const item = (o = {}) => ({
  immId: '1', immName: 'ТПА-4', moldName: 'ПФ', planQuantity: 10, actualQuantity: 8, cycleCount: 4,
  seconds: { Production: 3 * H, Setup: 0.5 * H, Downtime: H, Unplanned: 0, NoTask: 2 * H, Offline: 2 * H, NoData: 15.5 * H },
  efficiency: 46.15, rawMaterialKg: 1, downtimeDetails: [], timeline: [], ...o
})

async function mountWithReport(rows) {
  reportsApi.getDaily.mockResolvedValue({ data: { date: '2026-10-01', periodStart: '2026-10-01T05:00:00Z', periodEnd: '2026-10-02T05:00:00Z', immData: rows } })
  const w = mount(DailyReportView, {
    global: { plugins: [ElementPlus], components: { Download: { template: '<i />' } }, stubs: { GanttChart: true, 'el-date-picker': true } }
  })
  await flushPromises()
  await w.vm.loadReport()
  await flushPromises()
  return w
}

describe('DailyReportView — эффективные состояния', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('таблица: шесть колонок часов эффективных состояний', async () => {
    const w = await mountWithReport([item()])

    const labels = w.findAllComponents({ name: 'ElTableColumn' }).map(c => c.props('label'))
    expect(labels).toEqual(expect.arrayContaining([
      'Работа (ч)', 'Наладка (ч)', 'Простой (ч)', 'Работа без задания (ч)', 'Без задания (ч)', 'Нет связи (ч)'
    ]))
    expect(w.text()).toContain('3.0')   // Работа
    expect(w.text()).toContain('2.0')   // Без задания / Нет связи
    expect(w.text()).toContain('3.00.51.00.02.02.0')   // порядок колонок, один знак после запятой
  })

  it('эффективность форматируется как в отчёте оборудования', async () => {
    const w = await mountWithReport([item()])

    expect(w.find('[data-test="efficiency"]').text()).toBe('46%')
  })

  it('эффективность null → «—»', async () => {
    const w = await mountWithReport([item({ efficiency: null })])

    expect(w.find('[data-test="efficiency"]').text()).toBe('—')
  })
})

describe('DailyReportView — вкладки диаграммы и таблицы статусов', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('две вкладки, по умолчанию активна «Диаграмма»', async () => {
    const w = await mountWithReport([item()])

    const tabs = w.findComponent({ name: 'ElTabs' })
    expect(tabs.props('modelValue')).toBe('chart')
    const panes = w.findAllComponents({ name: 'ElTabPane' }).map(p => p.props('label'))
    expect(panes).toEqual(['Диаграмма', 'Таблица статусов'])
    expect(w.find('[data-test="reset-zoom"]').exists()).toBe(true)
  })

  it('вкладка «Таблица статусов» показывает группы по ТПА за период отчёта', async () => {
    const w = await mountWithReport([item()])

    await w.findComponent({ name: 'ElTabs' }).vm.$emit('update:modelValue', 'table')
    await flushPromises()

    const table = w.findComponent({ name: 'StatusTimelineTable' })
    expect(table.exists()).toBe(true)
    expect(table.props('periodStart')).toBe('2026-10-01T05:00:00Z')
    expect(table.props('periodEnd')).toBe('2026-10-02T05:00:00Z')
    expect(w.find('[data-test="reset-zoom"]').exists()).toBe(false)
  })
})
