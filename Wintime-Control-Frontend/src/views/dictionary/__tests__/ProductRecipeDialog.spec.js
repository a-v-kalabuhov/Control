// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus from 'element-plus'
import ProductRecipeDialog from '../ProductRecipeDialog.vue'
import { productTypesApi } from '@/api/productTypes'
import { materialsApi } from '@/api/materials'

vi.mock('@/api/productTypes', () => ({
  productTypesApi: { setRecipe: vi.fn(() => Promise.resolve({ data: {} })) }
}))
vi.mock('@/api/materials', () => ({
  materialsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'pp-1', type: 'Plastic', article: 'PP-1', name: 'Полипропилен', isActive: true },
      { id: 'pp-2', type: 'Plastic', article: 'PP-2', name: 'Полипропилен 2', isActive: true },
      { id: 'cr-1', type: 'Colorant', article: 'CR-1', name: 'Краситель', isActive: true }
    ] }))
  }
}))

function product(recipe = []) {
  return { id: 'prod-1', fullArticle: 'ART.0', name: 'По умолчанию', recipe }
}

function mountDialog(recipe) {
  return mount(ProductRecipeDialog, {
    props: { modelValue: true, typeId: 'type-1', product: product(recipe) },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

const fullRecipe = [
  { materialId: 'pp-1', materialType: 'Plastic', materialArticle: 'PP-1', materialName: 'Полипропилен', materialIsActive: true, percent: 60 },
  { materialId: 'pp-2', materialType: 'Plastic', materialArticle: 'PP-2', materialName: 'Полипропилен 2', materialIsActive: true, percent: 40 }
]

describe('ProductRecipeDialog', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => { document.body.innerHTML = '' })

  it('загружает строки рецепта изделия', async () => {
    const wrapper = mountDialog(fullRecipe)
    await nextTick()
    expect(wrapper.vm.rows).toHaveLength(2)
    expect(document.body.textContent).toContain('Пластики: 100 %')
  })

  it('«Добавить ингредиент» добавляет пустую строку', async () => {
    const wrapper = mountDialog([])
    await nextTick()
    document.body.querySelector('.recipe-add').click()
    await nextTick()
    expect(wrapper.vm.rows).toHaveLength(1)
  })

  it('больше 8 строк добавить нельзя', () => {
    const wrapper = mountDialog([])
    wrapper.vm.rows = Array.from({ length: 8 }, () => ({ materialId: null, percent: 12.5 }))
    expect(wrapper.vm.canAddRow).toBe(false)
  })

  it('уже выбранные материалы не предлагаются в других строках', async () => {
    const wrapper = mountDialog(fullRecipe)
    await wrapper.vm.loadMaterials()
    await flushPromises()
    const options = wrapper.vm.materialOptions(wrapper.vm.rows[0]).map(m => m.id)
    expect(options).toContain('pp-1')   // свой материал остаётся
    expect(options).not.toContain('pp-2')
    expect(options).toContain('cr-1')
  })

  it('сумма пластиков ≠ 100 % — красный итог, сохранение не блокируется', () => {
    const wrapper = mountDialog([fullRecipe[0]])
    expect(wrapper.vm.sumText).toContain('Пластики: 60 %')
    expect(wrapper.vm.sumIsOk).toBe(false)
    expect(wrapper.vm.canSave).toBe(true)
  })

  it('непустой рецепт без пластика — сохранение заблокировано', () => {
    const wrapper = mountDialog([
      { materialId: 'cr-1', materialType: 'Colorant', materialArticle: 'CR-1', materialName: 'Краситель', materialIsActive: true, percent: 2 }
    ])
    expect(wrapper.vm.noPlastic).toBe(true)
    expect(wrapper.vm.canSave).toBe(false)
  })

  it('«Сохранить» отправляет состав в порядке строк', async () => {
    const wrapper = mountDialog(fullRecipe)
    await nextTick()
    document.body.querySelector('.recipe-save').click()
    await flushPromises()
    expect(productTypesApi.setRecipe).toHaveBeenCalledWith('type-1', 'prod-1', [
      { materialId: 'pp-1', percent: 60 },
      { materialId: 'pp-2', percent: 40 }
    ])
    expect(wrapper.emitted('saved')).toBeTruthy()
  })

  it('архивный материал строки помечен и остаётся при сохранении', async () => {
    const wrapper = mountDialog([
      { materialId: 'old-1', materialType: 'Plastic', materialArticle: 'OLD', materialName: 'Старый', materialIsActive: false, percent: 100 }
    ])
    await nextTick()
    await flushPromises()
    expect(document.body.textContent).toContain('в архиве')

    document.body.querySelector('.recipe-save').click()
    await flushPromises()
    expect(productTypesApi.setRecipe).toHaveBeenCalledWith('type-1', 'prod-1', [{ materialId: 'old-1', percent: 100 }])
  })
})
