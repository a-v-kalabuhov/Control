// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage, ElCheckbox } from 'element-plus'
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

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

afterEach(() => vi.restoreAllMocks())

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

  it('при смене заказчика A на B поздний ответ A не заменяет строки B', async () => {
    const wrapper = mountView()
    await flushPromises()
    const a = deferred()
    const b = deferred()
    moldsApi.getList
      .mockImplementationOnce(() => a.promise)
      .mockImplementationOnce(() => b.promise)
    const rowA = { ...moldRow, id: 'race-a', formId: 'RACE-A' }
    const rowB = { ...moldRow, id: 'race-b', formId: 'RACE-B' }

    wrapper.vm.filters.customer = 'Заказчик A'
    const loadA = wrapper.vm.loadMolds()
    wrapper.vm.filters.customer = 'Заказчик B'
    const loadB = wrapper.vm.loadMolds()
    b.resolve({ data: [rowB] })
    await loadB
    await flushPromises()

    expect(wrapper.vm.molds).toEqual([rowB])
    expect(wrapper.vm.loading).toBe(false)

    a.resolve({ data: [rowA] })
    await loadA
    await flushPromises()

    expect(wrapper.vm.molds).toEqual([rowB])
    expect(wrapper.vm.loading).toBe(false)
  })

  it('устаревшая ошибка A не показывается и не сбрасывает loading запроса B', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const wrapper = mountView()
    await flushPromises()
    const a = deferred()
    const b = deferred()
    moldsApi.getList
      .mockImplementationOnce(() => a.promise)
      .mockImplementationOnce(() => b.promise)
    const rowB = { ...moldRow, id: 'race-b', formId: 'RACE-B' }

    wrapper.vm.filters.customer = 'Заказчик A'
    const loadA = wrapper.vm.loadMolds()
    wrapper.vm.filters.customer = 'Заказчик B'
    const loadB = wrapper.vm.loadMolds()
    a.reject(new Error('старая ошибка'))
    await loadA
    await flushPromises()

    expect(error).not.toHaveBeenCalled()
    expect(wrapper.vm.loading).toBe(true)

    b.resolve({ data: [rowB] })
    await loadB
    await flushPromises()
    expect(wrapper.vm.molds).toEqual([rowB])
    expect(wrapper.vm.loading).toBe(false)
  })

  it('после размонтирования pending rejection не показывает ошибку и не меняет состояние списка', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const pending = deferred()
    moldsApi.getList.mockImplementationOnce(() => pending.promise)
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.vm.loading).toBe(true)

    wrapper.unmount()
    pending.reject(new Error('сеть'))
    await flushPromises()

    expect(error).not.toHaveBeenCalled()
    expect(wrapper.vm.molds).toEqual([])
    expect(wrapper.vm.loading).toBe(true)
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

describe('MoldDictionary — особенности ПФ (PZP-26)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('в таблице показывает только включённые особенности и прочерк при их отсутствии', async () => {
    moldsApi.getList.mockResolvedValueOnce({ data: [
      { ...moldRow, hasMovingParts: true, requiresRobot: false, requiresConveyor: true },
      { ...moldRow, id: 'mold-2', formId: 'PF-2', hasMovingParts: false, requiresRobot: false, requiresConveyor: false }
    ] })
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Особенности')
    expect(wrapper.findAll('[data-test="mold-feature-moving-parts"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-test="mold-feature-robot"]')).toHaveLength(0)
    expect(wrapper.findAll('[data-test="mold-feature-conveyor"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-test="mold-features-empty"]')).toHaveLength(1)
  })

  it('задаёт признаки при создании, подставляет при редактировании и отправляет при сохранении', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.showCreateModal()
    expect(wrapper.vm.form.hasMovingParts).toBe(false)
    expect(wrapper.vm.form.requiresRobot).toBe(false)
    expect(wrapper.vm.form.requiresConveyor).toBe(false)

    wrapper.vm.editMold({ ...moldRow, hasMovingParts: true, requiresRobot: true, requiresConveyor: false })
    await flushPromises()
    expect(wrapper.vm.form.hasMovingParts).toBe(true)
    expect(wrapper.vm.form.requiresRobot).toBe(true)
    expect(wrapper.vm.form.requiresConveyor).toBe(false)

    wrapper.vm.form.requiresConveyor = true
    await wrapper.vm.saveMold()
    await flushPromises()
    expect(moldsApi.update).toHaveBeenCalledWith('mold-1', expect.objectContaining({
      hasMovingParts: true,
      requiresRobot: true,
      requiresConveyor: true
    }))
  })

  it('сбрасывает особенности при повторном создании после редактирования', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editMold({ ...moldRow, hasMovingParts: true, requiresRobot: true, requiresConveyor: true })
    await flushPromises()

    wrapper.vm.showCreateModal()
    await flushPromises()

    expect(wrapper.vm.form.hasMovingParts).toBe(false)
    expect(wrapper.vm.form.requiresRobot).toBe(false)
    expect(wrapper.vm.form.requiresConveyor).toBe(false)

    Object.assign(wrapper.vm.form, {
      formId: 'PF-NEW', name: 'Новая ПФ', productTypeId: 'pt-1', maxResourceCycles: 1000
    })
    await wrapper.vm.saveMold()
    await flushPromises()
    expect(moldsApi.create).toHaveBeenCalledWith(expect.objectContaining({
      hasMovingParts: false,
      requiresRobot: false,
      requiresConveyor: false
    }))
  })

  it('отключает чекбоксы особенностей только у архивной ПФ', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editMold({ ...moldRow, isActive: false })
    await flushPromises()
    let checkboxes = wrapper.findAllComponents(ElCheckbox)
    expect(checkboxes).toHaveLength(3)
    expect(checkboxes.every(c => c.props('disabled') === true)).toBe(true)

    wrapper.vm.form.isActive = true
    await flushPromises()
    checkboxes = wrapper.findAllComponents(ElCheckbox)
    expect(checkboxes.every(c => c.props('disabled') === true)).toBe(true)

    wrapper.vm.editMold(moldRow)
    await flushPromises()
    checkboxes = wrapper.findAllComponents(ElCheckbox)
    expect(checkboxes.every(c => c.props('disabled') === false)).toBe(true)
  })
})
