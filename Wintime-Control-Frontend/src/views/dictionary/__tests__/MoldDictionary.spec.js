// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import MoldDictionary from '../MoldDictionary.vue'
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'

vi.mock('@/api/molds', () => ({
  moldsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] })),
    create: vi.fn(() => Promise.resolve({ data: {} })),
    update: vi.fn(() => Promise.resolve({ data: {} })),
    getQr: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'pt-1', article: 'ART', name: 'Изделие', company: 'ООО Изделие', products: [
        { id: 'p-1', fullArticle: 'ART.0', partWeightGrams: 10, plannedInjectionCycleSeconds: 20 }
      ] },
      { id: 'pt-2', article: 'NEW', name: 'Другое изделие', company: 'ООО Другое', products: [
        { id: 'p-2', fullArticle: 'NEW.0', partWeightGrams: 20, plannedInjectionCycleSeconds: 30 }
      ] }
    ] })),
    suggestCompanies: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))
vi.mock('@/api/imm', () => ({
  immApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'imm-1', name: 'ТПА-1', isActive: true },
      { id: 'imm-2', name: 'ТПА-2', isActive: true }
    ] }))
  }
}))

import { moldsApi } from '@/api/molds'
import { immApi } from '@/api/imm'

const moldRow = {
  id: 'mold-1', formId: 'PF-1', name: 'Форма', cavities: 2, partWeightGrams: 1, runnerWeightGrams: 1,
  maxResourceCycles: 1000, remainingResource: 1000, to1Cycles: 0, to2Cycles: 0, storageLocationIndex: '',
  isActive: true, productTypeId: 'pt-1', productTypeArticle: 'ART', productTypeName: 'Изделие',
  compatibleImms: [
    { id: 'imm-1', name: 'ТПА-1', isActive: true },
    { id: 'imm-9', name: 'ТПА-9', isActive: false }
  ]
}

function mountView() {
  return mount(MoldDictionary, { global: { plugins: [ElementPlus], stubs: { Plus: true } } })
}

describe('MoldDictionary — совместимые ТПА (PZP-16)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('грузит только активные ТПА для выбора', async () => {
    mountView()
    await flushPromises()
    expect(immApi.getList).toHaveBeenCalledWith({ isActive: true })
  })

  it('при создании список пустой, опции — только активные ТПА', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.showCreateModal()
    expect(wrapper.vm.form.compatibleImmIds).toEqual([])
    expect(wrapper.vm.selectableImms.map(i => i.id)).toEqual(['imm-1', 'imm-2'])
  })

  it('при редактировании подставляет список ПФ и добавляет в опции её архивный ТПА', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editMold(moldRow)
    expect(wrapper.vm.form.compatibleImmIds).toEqual(['imm-1', 'imm-9'])
    expect(wrapper.vm.selectableImms.map(i => i.id)).toEqual(['imm-1', 'imm-2', 'imm-9'])
  })

  it('сохранение отправляет compatibleImmIds', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editMold(moldRow)
    await flushPromises()
    wrapper.vm.form.compatibleImmIds = ['imm-2']
    await wrapper.vm.saveMold()
    await flushPromises()

    expect(moldsApi.update).toHaveBeenCalledWith('mold-1',
      expect.objectContaining({ compatibleImmIds: ['imm-2'] }))
  })

  it('архивация ПФ не трогает список совместимых ТПА', async () => {
    const wrapper = mountView()
    await flushPromises()

    const { ElMessageBox } = await import('element-plus')
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValueOnce('confirm')
    await wrapper.vm.deleteMold(moldRow)
    await flushPromises()

    expect(moldsApi.update).toHaveBeenCalledWith('mold-1', { isActive: false })
  })
})

describe('MoldDictionary — заказчик типа (PZP-23)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('фильтр заказчика передаёт параметры через тип изделия и сохраняет остальные фильтры', async () => {
    const wrapper = mountView()
    await flushPromises()
    const listCalls = moldsApi.getList.mock.calls.length
    await wrapper.findComponent(CustomerFilterSelect).props('fetch')('Рома')
    expect(moldsApi.getList).toHaveBeenCalledTimes(listCalls)
    wrapper.vm.filters.search = 'Форма'
    wrapper.vm.filters.isActive = true

    wrapper.vm.filters.customer = 'ООО Ромашка'
    wrapper.vm.filters.customerEmpty = false
    await wrapper.vm.loadMolds()
    expect(moldsApi.getList).toHaveBeenLastCalledWith({
      isActive: true, search: 'Форма', customer: 'ООО Ромашка', customerEmpty: undefined
    })

    wrapper.vm.filters.customer = null
    wrapper.vm.filters.customerEmpty = true
    await wrapper.vm.loadMolds()
    expect(moldsApi.getList).toHaveBeenLastCalledWith({
      isActive: true, search: 'Форма', customer: undefined, customerEmpty: true
    })

    wrapper.vm.filters.customerEmpty = false
    await wrapper.vm.loadMolds()
    expect(moldsApi.getList).toHaveBeenLastCalledWith({
      isActive: true, search: 'Форма', customer: undefined, customerEmpty: undefined
    })
  })

  it('карточка ПФ показывает компанию выбранного типа изделия', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editMold({ ...moldRow, productTypeCompany: 'ООО Изделие' })
    await flushPromises()

    expect(wrapper.text()).toContain('Заказчик')
    expect(wrapper.text()).toContain('ООО Изделие')

    wrapper.vm.form.productTypeId = 'pt-2'
    await flushPromises()
    expect(wrapper.text()).toContain('ООО Другое')
  })
})

describe('MoldDictionary — веса изделий (PZP-24)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('при одинаковом весе изделий показывает одну строку, при разном — строку на изделие', async () => {
    const wrapper = mountView()
    await flushPromises()
    const products = [
      { productId: 'p-1', fullArticle: 'ART.0', partWeightGrams: 10, shotWeightGrams: 20 },
      { productId: 'p-2', fullArticle: 'ART.BLK', partWeightGrams: 10, shotWeightGrams: 20 }
    ]

    wrapper.vm.editMold({ ...moldRow, productTypeId: 'pt-archived', productWeights: products })
    expect(wrapper.vm.productWeightRows).toHaveLength(1)
    expect(wrapper.vm.productWeightRows[0].shotWeightGrams).toBe(20)

    products[1] = { ...products[1], partWeightGrams: 12.5, shotWeightGrams: 25 }
    wrapper.vm.editMold({ ...moldRow, productTypeId: 'pt-archived', productWeights: products })
    expect(wrapper.vm.productWeightRows).toHaveLength(2)
    expect(wrapper.vm.productWeightRows.map(r => r.fullArticle)).toEqual(['ART.0', 'ART.BLK'])
  })

  it('пересчитывает справку по редактируемым гнёздности и типу изделия', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editMold({
      ...moldRow,
      productWeights: [{ productId: 'p-1', fullArticle: 'ART.0', partWeightGrams: 10, shotWeightGrams: 20 }]
    })

    wrapper.vm.form.cavities = 4
    await flushPromises()
    expect(wrapper.vm.productWeightRows[0].shotWeightGrams).toBe(40)

    wrapper.vm.form.productTypeId = 'pt-2'
    await flushPromises()
    expect(wrapper.vm.productWeightRows[0].fullArticle).toBe('NEW.0')
    expect(wrapper.vm.productWeightRows[0].shotWeightGrams).toBe(80)
  })

  it('форма ПФ больше не отправляет вес детали', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editMold(moldRow)
    await flushPromises()
    await wrapper.vm.saveMold()
    await flushPromises()

    expect(wrapper.vm.form).not.toHaveProperty('partWeightGrams')
    expect(moldsApi.update.mock.calls[0][1]).not.toHaveProperty('partWeightGrams')
  })
})
