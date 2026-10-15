// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'

vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn() } }))
vi.mock('@/api/personnel', () => ({ personnelApi: { getList: vi.fn() } }))

const { immApi } = await import('@/api/imm')
const { personnelApi } = await import('@/api/personnel')
const { default: TaskFilters } = await import('../TaskFilters.vue')

// Подписи выбранных значений селектов (а не их id)
const selectedLabels = (w) => w.findAll('.el-select__placeholder:not(.is-transparent)').map(e => e.text())

describe('TaskFilters — сохранённые фильтры', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    immApi.getList.mockResolvedValue({ data: [{ id: 'imm-1', name: 'ТПА-4' }] })
    personnelApi.getList.mockResolvedValue({ data: [{ id: 'adj-1', fullName: 'Иванов Женя' }] })
  })

  it('выбранные ранее ТПА и наладчик показываются по названию, а не id', async () => {
    const w = mount(TaskFilters, {
      props: { filters: { status: null, immId: 'imm-1', personnelId: 'adj-1' } },
      global: { plugins: [ElementPlus] }
    })
    await flushPromises()

    const labels = selectedLabels(w)
    expect(labels).toContain('ТПА-4')
    expect(labels).toContain('Иванов Женя')
    expect(labels.join(' ')).not.toMatch(/imm-1|adj-1/)
  })

  it('без выбранных ТПА и наладчика списки не запрашиваются заранее', async () => {
    mount(TaskFilters, {
      props: { filters: { status: null, immId: null, personnelId: null } },
      global: { plugins: [ElementPlus] }
    })
    await flushPromises()

    expect(immApi.getList).not.toHaveBeenCalled()
    expect(personnelApi.getList).not.toHaveBeenCalled()
  })
})
