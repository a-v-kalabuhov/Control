// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ProductTypeDictionary from '../ProductTypeDictionary.vue'

vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(() => Promise.resolve({})),
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
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('показывает заголовок и кнопку добавления типа', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.text()).toContain('Справочник типов изделий')
    expect(wrapper.text()).toContain('Добавить тип изделия')
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

    expect(productTypesApi.addProduct).toHaveBeenCalledWith('pt-1', { article: 'BLK', name: 'Чёрный' })
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
})
