// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ProductTypeDictionary from '../ProductTypeDictionary.vue'
import NomenclatureImportDialog from '../NomenclatureImportDialog.vue'
import { productTypesApi } from '@/api/productTypes'
import { materialsApi } from '@/api/materials'

vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] })),
    suggestCompanies: vi.fn(() => Promise.resolve({ data: [] })),
    suggestArticle: vi.fn(() => Promise.resolve({ data: { article: null } }))
  }
}))

vi.mock('@/api/materials', () => ({
  materialsApi: { getList: vi.fn() }
}))

const plastics = [
  {
    id: 'plastic-1', type: 'Plastic', article: 'PP-1', name: 'ПолиПропилен',
    manufacturer: 'УникальныйЗавод', isActive: true
  },
  {
    id: 'plastic-2', type: 'Plastic', article: 'ABS-2', name: 'АБС',
    manufacturer: 'ПолиПропиленЗавод', isActive: false
  }
]
const colorants = [
  { id: 'colorant-1', type: 'Colorant', article: 'BLUE-1', name: 'Синий', isActive: true },
  { id: 'colorant-2', type: 'Colorant', article: 'OLD-2', name: 'Старый краситель', isActive: false }
]

const mounted = []
function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function mountView() {
  const wrapper = mount(ProductTypeDictionary, {
    global: { plugins: [ElementPlus], stubs: { Plus: true } },
    attachTo: document.body
  })
  mounted.push(wrapper)
  return wrapper
}

function optionLabels(select) {
  return select.findAllComponents({ name: 'ElOption' }).map(option => option.props('label'))
}

function optionValues(select) {
  return select.findAllComponents({ name: 'ElOption' }).map(option => option.props('value'))
}

describe('ProductTypeDictionary — фильтры по материалам (PZP-41)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    productTypesApi.getList.mockResolvedValue({ data: [] })
    materialsApi.getList.mockImplementation(({ type }) => Promise.resolve({
      data: type === 'Plastic' ? plastics : colorants
    }))
  })

  afterEach(() => {
    mounted.splice(0).forEach(wrapper => wrapper.unmount())
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('загружает активные и архивные материалы и показывает артикул, наименование и архив', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(materialsApi.getList).toHaveBeenCalledWith({ type: 'Plastic' })
    expect(materialsApi.getList).toHaveBeenCalledWith({ type: 'Colorant' })
    const plasticSelect = wrapper.findComponent('[data-test="plastic-filter"]')
    const colorantSelect = wrapper.findComponent('[data-test="colorant-filter"]')
    expect(plasticSelect.props('filterable')).toBe(true)
    expect(plasticSelect.props('clearable')).toBe(true)
    expect(colorantSelect.props('filterable')).toBe(true)
    expect(colorantSelect.props('clearable')).toBe(true)
    expect(optionLabels(plasticSelect)).toEqual([
      'PP-1 — ПолиПропилен',
      'ABS-2 — АБС (Архив)'
    ])
    expect(optionLabels(colorantSelect)).toEqual([
      'BLUE-1 — Синий',
      'OLD-2 — Старый краситель (Архив)'
    ])
  })

  it('ищет в списке по артикулу и наименованию без учёта регистра, но не по производителю', async () => {
    const wrapper = mountView()
    await flushPromises()
    const select = wrapper.findComponent('[data-test="plastic-filter"]')
    const filter = select.props('filterMethod')
    expect(filter).toBeTypeOf('function')

    filter('полипро')
    await wrapper.vm.$nextTick()
    expect(optionValues(select)).toEqual(['plastic-1'])

    filter('abs-')
    await wrapper.vm.$nextTick()
    expect(optionValues(select)).toEqual(['plastic-2'])

    filter('УникальныйЗавод')
    await wrapper.vm.$nextTick()
    expect(optionValues(select)).toEqual([])
  })

  it('сравнивает артикул независимо от локали браузера', async () => {
    materialsApi.getList.mockImplementation(({ type }) => Promise.resolve({
      data: type === 'Plastic'
        ? [{ id: 'plastic-i', type: 'Plastic', article: 'PI-1', name: 'Пластик', isActive: true }]
        : []
    }))
    const wrapper = mountView()
    await flushPromises()
    vi.spyOn(String.prototype, 'toLocaleLowerCase').mockImplementation(function () {
      return this.toString().replaceAll('I', 'ı').toLowerCase()
    })
    const select = wrapper.findComponent('[data-test="plastic-filter"]')

    select.props('filterMethod')('pi-')
    await wrapper.vm.$nextTick()

    expect(optionValues(select)).toEqual(['plastic-i'])
  })

  it('сохраняет успешно загруженный список, если другой тип материала не загрузился', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    materialsApi.getList.mockImplementation(({ type }) => type === 'Plastic'
      ? Promise.resolve({ data: plastics })
      : Promise.reject(new Error('network')))
    const wrapper = mountView()
    await flushPromises()

    expect(optionValues(wrapper.findComponent('[data-test="plastic-filter"]')))
      .toEqual(['plastic-1', 'plastic-2'])
    expect(optionValues(wrapper.findComponent('[data-test="colorant-filter"]'))).toEqual([])
    expect(error).toHaveBeenCalledWith('Ошибка загрузки материалов')
  })

  it('не показывает ошибку pending-загрузки после ухода со страницы', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const pending = deferred()
    materialsApi.getList.mockReturnValue(pending.promise)
    const wrapper = mountView()
    wrapper.unmount()

    pending.reject(new Error('network'))
    await flushPromises()

    expect(error).not.toHaveBeenCalled()
  })

  it('обновляет списки материалов после успешного Excel-импорта', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(materialsApi.getList).toHaveBeenCalledTimes(2)

    wrapper.vm.importDialogVisible = true
    await wrapper.vm.$nextTick()
    wrapper.findComponent(NomenclatureImportDialog).vm.$emit('imported')
    await flushPromises()

    expect(materialsApi.getList).toHaveBeenCalledTimes(4)
    expect(materialsApi.getList).toHaveBeenNthCalledWith(3, { type: 'Plastic' })
    expect(materialsApi.getList).toHaveBeenNthCalledWith(4, { type: 'Colorant' })
  })

  it('передаёт выбранные материалы вместе с остальными фильтрами и очищает их независимо', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.filters.search = 'Крышка'
    wrapper.vm.filters.isActive = true
    wrapper.vm.filters.customer = 'ООО Ромашка'

    await wrapper.findComponent('[data-test="plastic-filter"]').vm.$emit('update:modelValue', 'plastic-1')
    await wrapper.findComponent('[data-test="colorant-filter"]').vm.$emit('update:modelValue', 'colorant-1')
    await wrapper.vm.loadItems()

    expect(productTypesApi.getList).toHaveBeenLastCalledWith({
      isActive: true,
      search: 'Крышка',
      customer: 'ООО Ромашка',
      customerEmpty: undefined,
      plasticId: 'plastic-1',
      colorantId: 'colorant-1'
    })

    await wrapper.findComponent('[data-test="plastic-filter"]').vm.$emit('update:modelValue', null)
    await wrapper.vm.loadItems()
    expect(productTypesApi.getList).toHaveBeenLastCalledWith({
      isActive: true,
      search: 'Крышка',
      customer: 'ООО Ромашка',
      customerEmpty: undefined,
      colorantId: 'colorant-1'
    })

    await wrapper.findComponent('[data-test="colorant-filter"]').vm.$emit('update:modelValue', null)
    await wrapper.vm.loadItems()
    expect(productTypesApi.getList).toHaveBeenLastCalledWith({
      isActive: true,
      search: 'Крышка',
      customer: 'ООО Ромашка',
      customerEmpty: undefined
    })
  })
})
