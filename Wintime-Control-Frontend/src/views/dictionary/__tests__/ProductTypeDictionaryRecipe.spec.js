// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import ProductTypeDictionary from '../ProductTypeDictionary.vue'
import { productTypesApi } from '@/api/productTypes'

vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      {
        id: 'type-1', article: 'ART', name: 'Тип', isActive: true,
        products: [
          {
            id: 'p-1', article: 'BLK', fullArticle: 'ART.BLK', name: 'Чёрный', isActive: true, isUsed: false,
            recipe: [
              { materialId: 'pp-1', materialType: 'Plastic', materialArticle: 'PP-1', materialName: 'Полипропилен', materialIsActive: true, percent: 100 }
            ]
          },
          {
            id: 'p-2', article: '0', fullArticle: 'ART.0', name: 'По умолчанию', isActive: true, isUsed: false, recipe: []
          }
        ]
      }
    ] })),
    getById: vi.fn(() => Promise.resolve({ data: { id: 'type-1', article: 'ART', name: 'Тип', isActive: true, products: [] } })),
    update: vi.fn(() => Promise.resolve({ data: {} })),
    updateProduct: vi.fn(() => Promise.resolve({ data: {} })),
    addProduct: vi.fn(() => Promise.resolve({ data: {} })),
    deleteProduct: vi.fn(() => Promise.resolve({ data: {} })),
    setRecipe: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))

function mountView() {
  setActivePinia(createPinia())
  return mount(ProductTypeDictionary, {
    global: { plugins: [ElementPlus], stubs: { Plus: true } },
    attachTo: document.body
  })
}

describe('ProductTypeDictionary — рецепт (BL-36)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('в раскрытой строке типа есть колонка «Рецепт» с составом изделия', async () => {
    const wrapper = mountView()
    await flushPromises()
    await wrapper.find('.el-table__expand-icon').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Рецепт')
    expect(wrapper.text()).toContain('Полипропилен — 100 %')
  })

  it('показывает наименования материалов и переносит каждый ингредиент на новую строку', async () => {
    const wrapper = mountView()
    await flushPromises()
    const product = wrapper.vm.items[0].products[0]
    product.recipe = [
      { materialType: 'Plastic', materialArticle: 'ПП-1', materialName: 'Полипропилен', percent: 99 },
      { materialType: 'Colorant', materialArticle: 'КРАС-1', materialName: 'Синий краситель', percent: 1 }
    ]
    await wrapper.find('.el-table__expand-icon').trigger('click')
    await flushPromises()

    expect(wrapper.vm.recipeText(product)).toBe('Полипропилен — 99 %\nСиний краситель — 1 %')
    expect(wrapper.find('span.whitespace-pre-line').text())
      .toBe('Полипропилен — 99 %\nСиний краситель — 1 %')
  })

  it('показывает предупреждение в закрытой строке типа, если сумма неверна хотя бы в одном изделии', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.find('[data-test="type-recipe-warning"]').exists()).toBe(false)

    wrapper.vm.items[0].products[0].recipe[0].percent = 98
    await wrapper.vm.$nextTick()

    const warning = wrapper.find('[data-test="type-recipe-warning"]')
    expect(warning.exists()).toBe(true)
    expect(warning.text()).toBe('≠ 100 %')
    expect(wrapper.find('.el-table__expanded-cell').exists()).toBe(false)
  })

  it('не учитывает архивные изделия при отображении предупреждения у типа', async () => {
    const wrapper = mountView()
    await flushPromises()
    const archivedProduct = wrapper.vm.items[0].products[0]
    archivedProduct.isActive = false
    archivedProduct.recipe[0].percent = 98
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-test="type-recipe-warning"]').exists()).toBe(false)
  })

  it('пустой рецепт показан прочерком', async () => {
    const wrapper = mountView()
    await flushPromises()

    const empty = wrapper.vm.items[0].products.find(p => p.id === 'p-2')
    expect(wrapper.vm.recipeText(empty)).toBe('—')
  })

  it('сумма пластиков ≠ 100 % помечается', async () => {
    const wrapper = mountView()
    await flushPromises()

    const product = wrapper.vm.items[0].products[0]
    product.recipe[0].percent = 98
    expect(wrapper.vm.recipeSumOk(product)).toBe(false)
  })

  it('кнопка «Рецепт» открывает диалог для изделия', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.openRecipe(wrapper.vm.items[0].products[0])
    await flushPromises()

    expect(wrapper.vm.recipeDialogVisible).toBe(true)
    expect(wrapper.vm.recipeProduct.id).toBe('p-1')
  })

  it('после сохранения рецепта тип перечитывается', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editItem(wrapper.vm.items[0])
    await wrapper.vm.onRecipeSaved({ typeId: 'type-1', productId: 'p-1' })
    await flushPromises()

    expect(productTypesApi.getById).toHaveBeenCalled()
    expect(wrapper.vm.editing.products).toEqual([])
  })

  it('фоновое сохранение A обновляет список и не трогает открытый редактор B', async () => {
    const wrapper = mountView()
    await flushPromises()

    const typeB = { id: 'type-2', article: 'B', name: 'Другой тип', isActive: true, products: [] }
    wrapper.vm.editItem(typeB)
    const listCalls = productTypesApi.getList.mock.calls.length
    await wrapper.vm.onRecipeSaved({ typeId: 'type-1', productId: 'p-1' })
    await flushPromises()

    expect(productTypesApi.getList).toHaveBeenCalledTimes(listCalls + 1)
    expect(productTypesApi.getById).not.toHaveBeenCalled()
    expect(wrapper.vm.editing).toStrictEqual(typeB)
    expect(wrapper.vm.dialogVisible).toBe(true)
  })

  it('фоновое сохранение при закрытом редакторе обновляет список', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editing = null
    wrapper.vm.dialogVisible = false
    const listCalls = productTypesApi.getList.mock.calls.length
    await wrapper.vm.onRecipeSaved({ typeId: 'type-1', productId: 'p-1' })
    await flushPromises()

    expect(productTypesApi.getList).toHaveBeenCalledTimes(listCalls + 1)
    expect(productTypesApi.getById).not.toHaveBeenCalled()
    expect(wrapper.vm.editing).toBeNull()
  })

  it('поздний ответ перечитывания не перезаписывает другой открытый тип', async () => {
    let resolveReload
    productTypesApi.getById.mockImplementationOnce(() => new Promise(resolve => {
      resolveReload = resolve
    }))
    const wrapper = mountView()
    await flushPromises()

    const typeA = wrapper.vm.items[0]
    const typeB = { id: 'type-2', article: 'B', name: 'Другой тип', isActive: true, products: [] }
    wrapper.vm.editItem(typeA)
    const reload = wrapper.vm.onRecipeSaved({ typeId: 'type-1', productId: 'p-1' })
    wrapper.vm.editItem(typeB)

    resolveReload({ data: { ...typeA, name: 'Поздний ответ A' } })
    await reload
    await flushPromises()

    expect(wrapper.vm.editing.id).toBe('type-2')
    expect(wrapper.vm.editing.name).toBe('Другой тип')
  })
})
