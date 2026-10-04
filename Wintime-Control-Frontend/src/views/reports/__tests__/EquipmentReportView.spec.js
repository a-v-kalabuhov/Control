// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import { nextTick } from 'vue'
import dayjs from 'dayjs'

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

function mountView(options = {}) {
  return mount(EquipmentReportView, {
    ...options,
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

  it('период по умолчанию — ровно 7 суток, включая сегодня', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    mountView()
    await flushPromises()

    const params = reportsApi.getEquipment.mock.calls[0][0]
    expect(params.dateFrom).toBe(dayjs().subtract(6, 'day').format('YYYY-MM-DD'))
    expect(params.dateTo).toBe(dayjs().format('YYYY-MM-DD'))
  })

  it('после смены фильтров показывает пометку об устаревании до повторного формирования', async () => {
    immApi.getList.mockResolvedValue({ data: [
      { id: '1', name: 'A', isActive: true }, { id: '2', name: 'B', isActive: true }
    ] })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-test="stale-hint"]').exists()).toBe(false)

    w.vm.selectedImmIds = ['1']
    await nextTick()
    expect(w.find('[data-test="stale-hint"]').exists()).toBe(true)

    // Вернули фильтр как был — отчёт снова актуален
    w.vm.selectedImmIds = ['1', '2']
    await nextTick()
    expect(w.find('[data-test="stale-hint"]').exists()).toBe(false)

    w.vm.selectedImmIds = ['1']
    await nextTick()
    await w.find('[data-test="load-report"]').trigger('click')
    await flushPromises()
    expect(w.find('[data-test="stale-hint"]').exists()).toBe(false)
  })

  it('вкладки: по умолчанию диаграммы; таблица — на своей вкладке, вкладка не сбрасывается при формировании', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    const w = mountView()
    await flushPromises()
    expect(w.findAllComponents({ name: 'EquipmentImmRow' })).toHaveLength(2)
    expect(w.find('[data-test="summary-table"]').exists()).toBe(false)

    w.vm.activeTab = 'table'
    await nextTick()
    expect(w.findAllComponents({ name: 'EquipmentImmRow' })).toHaveLength(0)
    expect(w.find('[data-test="summary-table"]').exists()).toBe(true)

    await w.find('[data-test="load-report"]').trigger('click')
    await flushPromises()
    expect(w.vm.activeTab).toBe('table')
    expect(w.find('[data-test="summary-table"]').exists()).toBe(true)
  })

  it('клик по строке таблицы открывает диаграммы и прокручивает к карточке этого ТПА', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    const scrolled = []
    Element.prototype.scrollIntoView = vi.fn(function () { scrolled.push(this.getAttribute('data-imm-id')) })
    const w = mountView({ attachTo: document.body })
    await flushPromises()

    w.vm.activeTab = 'table'
    await nextTick()
    await flushPromises()
    const rows = w.findAll('.el-table__body tr.el-table__row')
    expect(rows).toHaveLength(2)
    await rows[1].trigger('click')
    await flushPromises()

    expect(w.vm.activeTab).toBe('charts')
    expect(scrolled).toEqual(['2'])
    const highlighted = w.findAllComponents({ name: 'EquipmentImmRow' }).filter(c => c.classes('imm-highlight'))
    expect(highlighted.map(c => c.attributes('data-imm-id'))).toEqual(['2'])
    w.unmount()
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
