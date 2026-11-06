// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ProductTypeDictionary from '../ProductTypeDictionary.vue'
import SuggestInput from '@/components/common/SuggestInput.vue'
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'
import NomenclatureImportDialog from '../NomenclatureImportDialog.vue'

vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(() => Promise.resolve({})),
    suggestCompanies: vi.fn(() => Promise.resolve({ data: [] })),
    addProduct: vi.fn(),
    updateProduct: vi.fn(() => Promise.resolve({})),
    deleteProduct: vi.fn(() => Promise.resolve({}))
  }
}))

import { productTypesApi } from '@/api/productTypes'

const typeA = {
  id: 'pt-1', article: 'A123', name: 'Крышка', isActive: true,
  products: [
    { id: 'p1', article: '0', fullArticle: 'A123.0', name: 'По умолчанию', isActive: true, isUsed: true },
    { id: 'p2', article: 'BLK', fullArticle: 'A123.BLK', name: 'Чёрный', isActive: true, isUsed: false },
    { id: 'p3', article: 'OLD', fullArticle: 'A123.OLD', name: 'Старый', isActive: false, isUsed: false }
  ]
}

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
  const w = mount(ProductTypeDictionary, {
    global: { plugins: [ElementPlus], stubs: { Plus: true } },
    attachTo: document.body
  })
  mounted.push(w)
  return w
}

describe('ProductTypeDictionary (PZP-19)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    productTypesApi.getList.mockResolvedValue({ data: [typeA] })
    productTypesApi.getById.mockResolvedValue({ data: typeA })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('показывает заголовок и кнопку добавления типа', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.text()).toContain('Справочник типов изделий')
    expect(wrapper.text()).toContain('Добавить тип изделия')
  })

  it('показывает импорт и перезагружает справочник после успешного импорта', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.text()).toContain('Импорт Excel')
    const initialCalls = productTypesApi.getList.mock.calls.length

    wrapper.vm.importDialogVisible = true
    await wrapper.vm.$nextTick()
    wrapper.findComponent(NomenclatureImportDialog).vm.$emit('imported')
    await flushPromises()

    expect(productTypesApi.getList.mock.calls.length).toBe(initialCalls + 1)
  })

  it('показывает число активных изделий типа', async () => {
    const wrapper = mountView()
    await flushPromises()
    const cells = wrapper.findAll('.products-count')
    expect(cells[0].text()).toBe('2')
  })

  it('после создания окно остаётся в режиме редактирования с изделием 0', async () => {
    const created = {
      id: 'pt-2', article: 'NEW', name: 'Новый', isActive: true,
      products: [{ id: 'n0', article: '0', fullArticle: 'NEW.0', name: 'По умолчанию', isActive: true, isUsed: false }]
    }
    productTypesApi.create.mockResolvedValue({ data: created })
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.showCreateModal()
    wrapper.vm.form.article = 'NEW'
    wrapper.vm.form.name = 'Новый'
    await flushPromises()
    await wrapper.vm.save()
    await flushPromises()

    expect(wrapper.vm.editing).toEqual(created)
    expect(wrapper.vm.dialogVisible).toBe(true)
    expect(document.body.textContent).toContain('Изделия')
    expect(document.body.textContent).toContain('NEW.0')
  })

  it('форма типа редактирует компанию через подсказки и отправляет её в API', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem({ ...typeA, company: 'ООО Заказчик' })
    await flushPromises()

    expect(wrapper.vm.form.company).toBe('ООО Заказчик')
    const suggest = wrapper.findComponent(SuggestInput)
    expect(suggest.exists()).toBe(true)
    await suggest.props('fetch')('Заказ')
    expect(productTypesApi.suggestCompanies).toHaveBeenCalledWith('Заказ')

    wrapper.vm.form.company = 'ООО Новый заказчик'
    await wrapper.vm.save()
    await flushPromises()

    expect(productTypesApi.update).toHaveBeenCalledWith('pt-1',
      expect.objectContaining({ company: 'ООО Новый заказчик' }))
  })

  it('фильтр заказчика передаёт customer/customerEmpty и сохраняет остальные фильтры', async () => {
    const wrapper = mountView()
    await flushPromises()
    const listCalls = productTypesApi.getList.mock.calls.length
    await wrapper.findComponent(CustomerFilterSelect).props('fetch')('Рома')
    expect(productTypesApi.suggestCompanies).toHaveBeenCalledWith('Рома')
    expect(productTypesApi.getList).toHaveBeenCalledTimes(listCalls)
    wrapper.vm.filters.search = 'Крышка'
    wrapper.vm.filters.isActive = true

    wrapper.vm.filters.customer = 'ООО Ромашка'
    wrapper.vm.filters.customerEmpty = false
    await wrapper.vm.loadItems()
    expect(productTypesApi.getList).toHaveBeenLastCalledWith({
      isActive: true, search: 'Крышка', customer: 'ООО Ромашка', customerEmpty: undefined
    })

    wrapper.vm.filters.customer = null
    wrapper.vm.filters.customerEmpty = true
    await wrapper.vm.loadItems()
    expect(productTypesApi.getList).toHaveBeenLastCalledWith({
      isActive: true, search: 'Крышка', customer: undefined, customerEmpty: true
    })

    wrapper.vm.filters.customerEmpty = false
    await wrapper.vm.loadItems()
    expect(productTypesApi.getList).toHaveBeenLastCalledWith({
      isActive: true, search: 'Крышка', customer: undefined, customerEmpty: undefined
    })
  })

  it('при смене заказчика A на B поздний ответ A не заменяет строки B', async () => {
    const wrapper = mountView()
    await flushPromises()
    const a = deferred()
    const b = deferred()
    productTypesApi.getList
      .mockImplementationOnce(() => a.promise)
      .mockImplementationOnce(() => b.promise)
    const rowA = { ...typeA, id: 'race-a', article: 'RACE-A' }
    const rowB = { ...typeA, id: 'race-b', article: 'RACE-B' }

    wrapper.vm.filters.customer = 'Заказчик A'
    const loadA = wrapper.vm.loadItems()
    wrapper.vm.filters.customer = 'Заказчик B'
    const loadB = wrapper.vm.loadItems()
    b.resolve({ data: [rowB] })
    await loadB
    await flushPromises()

    expect(wrapper.vm.items).toEqual([rowB])
    expect(wrapper.vm.loading).toBe(false)

    a.resolve({ data: [rowA] })
    await loadA
    await flushPromises()

    expect(wrapper.vm.items).toEqual([rowB])
    expect(wrapper.vm.loading).toBe(false)
  })

  it('устаревшая ошибка A не показывается и не сбрасывает loading запроса B', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const wrapper = mountView()
    await flushPromises()
    const a = deferred()
    const b = deferred()
    productTypesApi.getList
      .mockImplementationOnce(() => a.promise)
      .mockImplementationOnce(() => b.promise)
    const rowB = { ...typeA, id: 'race-b', article: 'RACE-B' }

    wrapper.vm.filters.customer = 'Заказчик A'
    const loadA = wrapper.vm.loadItems()
    wrapper.vm.filters.customer = 'Заказчик B'
    const loadB = wrapper.vm.loadItems()
    a.reject(new Error('старая ошибка'))
    await loadA
    await flushPromises()

    expect(error).not.toHaveBeenCalled()
    expect(wrapper.vm.loading).toBe(true)

    b.resolve({ data: [rowB] })
    await loadB
    await flushPromises()
    expect(wrapper.vm.items).toEqual([rowB])
    expect(wrapper.vm.loading).toBe(false)
  })

  it('после размонтирования pending rejection не показывает ошибку и не меняет состояние списка', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const pending = deferred()
    productTypesApi.getList.mockImplementationOnce(() => pending.promise)
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.vm.loading).toBe(true)

    wrapper.unmount()
    pending.reject(new Error('сеть'))
    await flushPromises()

    expect(error).not.toHaveBeenCalled()
    expect(wrapper.vm.items).toEqual([])
    expect(wrapper.vm.loading).toBe(true)
  })

  it('при открытии нового изделия подставляет следующий числовой артикул', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)

    wrapper.vm.showProductModal()
    await flushPromises()

    expect(wrapper.vm.editingProduct).toBeNull()
    expect(wrapper.vm.productForm.article).toBe('1')
    expect(wrapper.vm.fullArticlePreview).toBe('A123.1')
  })

  it('сохраняет ручную замену автоподставленного артикула', async () => {
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    expect(wrapper.vm.productForm.article).toBe('1')

    wrapper.vm.productForm.article = 'custom'
    wrapper.vm.onProductArticleInput()
    wrapper.vm.productForm.name = 'Особый'
    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', expect.objectContaining({
      article: 'CUSTOM'
    }))
  })

  it('при редактировании не заменяет существующий артикул автоподстановкой', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)

    wrapper.vm.editProduct({ id: 'p4', article: '4', name: 'Четвёртый' })
    await flushPromises()

    expect(wrapper.vm.productForm.article).toBe('4')
  })

  it('сохраняет комментарий изделия в create и update payload', async () => {
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    wrapper.vm.productForm.article = 'BLK'
    wrapper.vm.productForm.name = 'Чёрный'
    wrapper.vm.productForm.comment = 'Не сушить'

    await wrapper.vm.saveProduct()
    await flushPromises()
    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', expect.objectContaining({
      comment: 'Не сушить'
    }))

    wrapper.vm.editProduct({ ...typeA.products[1], comment: 'Старый комментарий' })
    expect(wrapper.vm.productForm.comment).toBe('Старый комментарий')
    wrapper.vm.productForm.comment = 'Новый комментарий'
    await wrapper.vm.saveProduct()
    await flushPromises()
    expect(productTypesApi.updateProduct).toHaveBeenCalledWith('pt-1', 'p2', expect.objectContaining({
      comment: 'Новый комментарий'
    }))

    wrapper.vm.productForm.comment = ''
    await wrapper.vm.saveProduct()
    await flushPromises()
    expect(productTypesApi.updateProduct).toHaveBeenLastCalledWith('pt-1', 'p2', expect.objectContaining({
      comment: null, clearComment: true
    }))
  })

  it('добавление изделия приводит часть к верхнему регистру и перезагружает тип', async () => {
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    wrapper.vm.productForm.article = 'blk'
    wrapper.vm.onProductArticleInput()
    wrapper.vm.productForm.name = 'Чёрный'
    await flushPromises()

    expect(wrapper.vm.productForm.article).toBe('BLK')
    expect(wrapper.vm.fullArticlePreview).toBe('A123.BLK')

    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', {
      article: 'BLK', name: 'Чёрный', comment: null, partWeightGrams: 0, cavities: null, plannedInjectionCycleSeconds: null, hasSample: false
    })
    expect(productTypesApi.getById).toHaveBeenCalledWith('pt-1')
  })

  it('кнопка «Удалить» только у неиспользуемого изделия', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    await flushPromises()

    const deletes = [...document.body.querySelectorAll('.product-delete')]
    expect(deletes.length).toBe(2) // p2 и p3 (isUsed=false), p1 — нет
  })

  it('ошибка сервера показывается текстом сервера', async () => {
    const spy = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    productTypesApi.addProduct.mockRejectedValue({ response: { data: 'Полный артикул A123.BLK уже есть в типе' } })
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    wrapper.vm.productForm.article = 'BLK'
    wrapper.vm.productForm.name = 'Чёрный'
    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(spy).toHaveBeenCalledWith('Полный артикул A123.BLK уже есть в типе')
  })

  it('восстановление типа из архива: текст 409 сервера показывается пользователю', async () => {
    const spy = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const text = 'Сначала верните из архива хотя бы одно изделие типа'
    productTypesApi.update.mockRejectedValueOnce({ response: { data: text } })
    const wrapper = mountView()
    await flushPromises()

    await wrapper.vm.toggleArchive({ ...typeA, isActive: false })
    await flushPromises()

    expect(spy).toHaveBeenCalledWith(text)
  })

  it('нестроковое тело ошибки не попадает в сообщение: показывается запасной текст', async () => {
    const spy = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    productTypesApi.update.mockRejectedValueOnce({ response: { data: { errors: {} } } })
    const wrapper = mountView()
    await flushPromises()

    await wrapper.vm.toggleArchive(typeA)
    await flushPromises()

    expect(spy).toHaveBeenCalledWith('Ошибка изменения статуса')
  })

  it('сбой перезагрузки после сохранения изделия не выдаётся за ошибку сохранения', async () => {
    const err = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const ok = vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    wrapper.vm.productForm.article = 'BLK'
    wrapper.vm.productForm.name = 'Чёрный'
    productTypesApi.getById.mockRejectedValueOnce(new Error('network'))

    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(ok).toHaveBeenCalledWith('Изделие добавлено')
    expect(err).not.toHaveBeenCalledWith('Ошибка сохранения изделия')
    expect(err).toHaveBeenCalledWith('Не удалось обновить данные типа')
  })
})

describe('ProductTypeDictionary — атрибуты изделия (PZP-24)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    productTypesApi.getList.mockResolvedValue({ data: [typeA] })
    productTypesApi.getById.mockResolvedValue({ data: typeA })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('при добавлении передаёт вес детали и эталонный цикл литья', async () => {
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    Object.assign(wrapper.vm.productForm, {
      article: 'blk', name: 'Чёрный', partWeightGrams: 12.5, plannedInjectionCycleSeconds: 20
    })

    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', {
      article: 'BLK', name: 'Чёрный', comment: null, partWeightGrams: 12.5, cavities: null, plannedInjectionCycleSeconds: 20, hasSample: false
    })
  })

  it('при правке сохраняет вес и явно очищает цикл литья', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.editProduct({
      id: 'p-1', article: 'BLK', name: 'Чёрный', partWeightGrams: 12.5, cavities: 2, plannedInjectionCycleSeconds: 20
    })
    expect(wrapper.vm.productForm.cavities).toBe(2)
    Object.assign(wrapper.vm.productForm, { partWeightGrams: 14.25, cavities: null, plannedInjectionCycleSeconds: null })

    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(productTypesApi.updateProduct).toHaveBeenCalledWith('pt-1', 'p-1', {
      article: 'BLK', name: 'Чёрный', comment: null, clearComment: true, partWeightGrams: 14.25, cavities: null, clearCavities: true,
      plannedInjectionCycleSeconds: null, clearPlannedInjectionCycleSeconds: true, hasSample: false
    })
  })
})

describe('ProductTypeDictionary — гнёздовость изделия (PZP-36)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    productTypesApi.getList.mockResolvedValue({ data: [typeA] })
    productTypesApi.getById.mockResolvedValue({ data: typeA })
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('поле умолчания гнёзд — целое, необязательное, от одного', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    await flushPromises()

    const input = wrapper.findComponent('[data-test="product-cavities"]')
    expect(input.exists()).toBe(true)
    expect(input.props('min')).toBe(1)
    expect(input.props('precision')).toBe(0)
    expect(wrapper.vm.productForm.cavities).toBeNull()
  })

  it('при добавлении передаёт умолчание гнёзд изделия', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    Object.assign(wrapper.vm.productForm, { article: 'PAIR', name: 'Парная', cavities: 2 })

    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', expect.objectContaining({ cavities: 2 }))
  })
})

describe('ProductTypeDictionary — образец и тарная единица (PZP-29)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    productTypesApi.getList.mockResolvedValue({ data: [typeA] })
    productTypesApi.getById.mockResolvedValue({ data: typeA })
    productTypesApi.create.mockResolvedValue({ data: { ...typeA, id: 'pt-new' } })
    productTypesApi.addProduct.mockResolvedValue({ data: {} })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('при создании типа передаёт тарную единицу', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.showCreateModal()
    Object.assign(wrapper.vm.form, { article: 'PACK', name: 'Тарный тип', packagingUnit: 24 })
    await flushPromises()

    await wrapper.vm.save()
    await flushPromises()

    expect(productTypesApi.create).toHaveBeenCalledWith(expect.objectContaining({ packagingUnit: 24 }))
  })

  it('при очистке тарной единицы отправляет явный clear-флаг', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem({ ...typeA, packagingUnit: 24 })
    expect(wrapper.vm.form.packagingUnit).toBe(24)
    wrapper.vm.form.packagingUnit = null
    await flushPromises()

    await wrapper.vm.save()
    await flushPromises()

    expect(productTypesApi.update).toHaveBeenCalledWith('pt-1', expect.objectContaining({
      packagingUnit: null, clearPackagingUnit: true
    }))
  })

  it('поле тарной единицы настроено на целые числа', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem({ ...typeA, packagingUnit: 24 })
    await flushPromises()

    expect(wrapper.findComponent('[data-test="type-packaging-unit"]').props('precision')).toBe(0)
  })

  it('при добавлении изделия передаёт признак образца', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem(typeA)
    wrapper.vm.showProductModal()
    Object.assign(wrapper.vm.productForm, { article: 'BLK', name: 'Чёрный', hasSample: true })

    await wrapper.vm.saveProduct()
    await flushPromises()

    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', expect.objectContaining({ hasSample: true }))
  })

  it('показывает значок только у изделия с образцом', async () => {
    const wrapper = mountView()
    await flushPromises()
    wrapper.vm.editItem({
      ...typeA,
      products: [
        { ...typeA.products[0], hasSample: true },
        { ...typeA.products[1], hasSample: false }
      ]
    })
    await flushPromises()

    expect(document.body.querySelectorAll('[data-test="product-sample"]')).toHaveLength(1)
  })
})
