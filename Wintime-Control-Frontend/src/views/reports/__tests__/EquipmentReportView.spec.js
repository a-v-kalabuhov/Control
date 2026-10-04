// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn() } }))
vi.mock('@/api/reports', () => ({ reportsApi: { getEquipment: vi.fn(), exportExcel: vi.fn() } }))

const { immApi } = await import('@/api/imm')
const { reportsApi } = await import('@/api/reports')
const { default: EquipmentReportView } = await import('../EquipmentReportView.vue')

const H = 3600
const row = (id, name, isActive = true) => ({
  immId: id, immName: name, isActive,
  seconds: { Production: 12 * H, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 12 * H, Offline: 0, NoData: 0 },
  totalCycles: 10, avgCycleSeconds: 30, efficiency: 50, days: []
})

function mountView() {
  return mount(EquipmentReportView, {
    global: {
      plugins: [ElementPlus],
      stubs: { EquipmentImmRow: true, 'el-date-picker': true, 'el-select': true, 'el-option': true }
    }
  })
}

describe('EquipmentReportView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    reportsApi.getEquipment.mockResolvedValue({ data: { immData: [row('1', 'A'), row('2', 'B')] } })
  })

  it('по умолчанию запрашивает все неархивные ТПА (без immIds) и рисует строку на каждый ТПА', async () => {
    immApi.getList.mockResolvedValue({ data: [
      { id: '1', name: 'A', isActive: true },
      { id: '2', name: 'B', isActive: true },
      { id: '3', name: 'X', isActive: false },
    ] })
    const w = mountView()
    await flushPromises()

    const params = reportsApi.getEquipment.mock.calls[0][0]
    expect(params.immIds).toBeUndefined()   // выбраны все — immIds не отправляется
    expect(params.archive).toBe('exclude')
    expect(w.findAllComponents({ name: 'EquipmentImmRow' })).toHaveLength(2)
  })

  it('переключатель архива скрыт, если архивных ТПА нет', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-test="archive-filter"]').exists()).toBe(false)
  })

  it('переключатель архива показан, если архивные ТПА есть', async () => {
    immApi.getList.mockResolvedValue({ data: [
      { id: '1', name: 'A', isActive: true }, { id: '3', name: 'X', isActive: false }
    ] })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-test="archive-filter"]').exists()).toBe(true)
  })

  it('KPI: взвешенная эффективность парка и две строки проблемных', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    const w = mountView()
    await flushPromises()
    const kpi = w.find('[data-test="kpi"]').text()
    expect(kpi).toContain('50%')        // Σ 24 ч Работы / Σ 48 ч известного
    expect(kpi).toContain('< 70 %: 2')
    expect(kpi).toContain('< 50 %: 0')
  })
})
