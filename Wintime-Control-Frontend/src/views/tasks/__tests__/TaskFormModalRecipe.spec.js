// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import TaskFormModal from '../TaskFormModal.vue'
import { ordersApi } from '@/api/orders'

vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/molds', () => ({
  moldsApi: { getList: vi.fn(() => Promise.resolve({ data: [
    { id: 'mold-1', name: 'Форма 1', cavities: 4, isActive: true, productTypeId: 'pt-X', productTypeArticle: 'ART-X' }
  ] })) }
}))
vi.mock('@/api/personnel', () => ({ personnelApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/tasks', () => ({
  tasksApi: {
    create: vi.fn(() => Promise.resolve({ data: {} })),
    update: vi.fn(() => Promise.resolve({ data: {} })),
    setPersonnel: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))
vi.mock('@/api/orders', () => ({
  ordersApi: { getList: vi.fn(() => Promise.resolve({ data: [] })), setTaskOrder: vi.fn(() => Promise.resolve({ data: {} })) }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: { getList: vi.fn(() => Promise.resolve({ data: [
    {
      id: 'pt-X', article: 'ART-X', name: 'Тип X', isActive: true,
      products: [
        { id: 'p-X0', article: '0', fullArticle: 'ART-X.0', name: 'По умолчанию', isActive: true, recipe: [
          { materialId: 'pp-1', materialType: 'Plastic', materialArticle: 'PP-1', materialName: 'Полипропилен', materialIsActive: true, percent: 100 }
        ] },
        { id: 'p-XB', article: 'BLK', fullArticle: 'ART-X.BLK', name: 'Чёрный', isActive: true, recipe: [
          { materialId: 'pp-2', materialType: 'Plastic', materialArticle: 'PP-2', materialName: 'Полипропилен 2', materialIsActive: true, percent: 98 }
        ] },
        { id: 'p-XA', article: 'ARCH', fullArticle: 'ART-X.ARCH', name: 'Архивное', isActive: false, recipe: [
          { materialId: 'pp-arch', materialType: 'Plastic', materialArticle: 'PP-ARCH', materialName: 'Архивный пластик', materialIsActive: false, percent: 100 }
        ] }
      ]
    }
  ] })) }
}))
vi.mock('@/api/planning', () => ({
  planningApi: { getConflicts: vi.fn(() => Promise.resolve({ data: { taskCount: 0, planCount: 0, deviationCount: 0, items: [] } })) }
}))
vi.mock('@/api/settings', () => ({
  settingsApi: { getPlanningDefaults: vi.fn(() => Promise.resolve({ data: {
    setupMinutes: 30, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10, updatedAt: null
  } })) }
}))

function mountModal(task = null) {
  setActivePinia(createPinia())
  return mount(TaskFormModal, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

const editedTask = {
  id: 'task-1', status: 'Planned', immId: 'imm-1', moldId: 'mold-1', productId: 'p-X0',
  planQuantity: 10, recipeEdited: true,
  recipeIngredients: [
    { id: 'ri-1', materialId: 'pp-2', materialType: 'Plastic', materialArticle: 'PP-2', materialName: 'Полипропилен 2',
      originalMaterialId: 'pp-1', originalMaterialArticle: 'PP-1', originalMaterialName: 'Полипропилен', percent: 100 }
  ]
}

describe('TaskFormModal — рецепт (BL-36)', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => { document.body.innerHTML = '' })

  it('показывает рецепт выбранного изделия', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    await flushPromises()
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()

    expect(wrapper.vm.selectedProductRecipe).toHaveLength(1)
    expect(wrapper.vm.recipeText).toBe('PP-1 — 100 %')
    expect(document.body.textContent).toContain('Рецепт изделия')
  })

  it('красный значок при сумме пластиков ≠ 100 %', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    await flushPromises()
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()
    wrapper.vm.form.productId = 'p-XB'
    await flushPromises()

    expect(wrapper.vm.recipeSumOk).toBe(false)
    expect(document.body.textContent).toContain('98 %')
  })

  it('смена изделия у задания с правленым рецептом спрашивает подтверждение', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const wrapper = mountModal(editedTask)
    await flushPromises()

    wrapper.vm.form.productId = 'p-XB'
    await flushPromises()

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(confirm.mock.calls[0][0]).toContain('Рецепт в задании был изменён')
    expect(wrapper.vm.form.productId).toBe('p-XB')
  })

  it('«Нет» возвращает прежнее изделие', async () => {
    vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue('cancel')
    const wrapper = mountModal(editedTask)
    await flushPromises()

    wrapper.vm.form.productId = 'p-XB'
    await flushPromises()

    expect(wrapper.vm.form.productId).toBe('p-X0')
  })

  it('задание без правки рецепта — смена изделия без диалога', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm')
    const wrapper = mountModal({ ...editedTask, recipeEdited: false })
    await flushPromises()

    wrapper.vm.form.productId = 'p-XB'
    await flushPromises()

    expect(confirm).not.toHaveBeenCalled()
  })

  it('новое задание — смена изделия без диалога', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm')
    const wrapper = mountModal(null)
    await wrapper.vm.loadMolds()
    await flushPromises()
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.productId = 'p-X0'
    await flushPromises()

    wrapper.vm.form.productId = 'p-XB'
    await flushPromises()

    expect(confirm).not.toHaveBeenCalled()
  })

  it('отмена смены изделия сохраняет заказ и игнорирует поздний ответ для нового изделия', async () => {
    const orderA = { id: 'order-A', number: 'A-1', productId: 'p-X0' }
    const orderB = { id: 'order-B', number: 'B-1', productId: 'p-XB' }
    let resolveB
    const responseB = new Promise(resolve => { resolveB = resolve })
    ordersApi.getList
      .mockResolvedValueOnce({ data: [orderA] })
      .mockImplementationOnce(() => responseB)
    vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue('cancel')
    const wrapper = mountModal({ ...editedTask, orderId: orderA.id })
    await flushPromises()

    wrapper.vm.form.productId = 'p-XB'
    await flushPromises()
    resolveB({ data: [orderB] })
    await flushPromises()

    expect(wrapper.vm.form.productId).toBe('p-X0')
    expect(wrapper.vm.form.orderId).toBe(orderA.id)
    expect(wrapper.vm.orderOptions).toEqual([orderA])
    expect(ordersApi.getList).toHaveBeenCalledTimes(2)
  })

  it('показывает рецепт архивного изделия текущего задания', async () => {
    const wrapper = mountModal({
      ...editedTask,
      productId: 'p-XA',
      productFullArticle: 'ART-X.ARCH',
      productName: 'Архивное',
      recipeEdited: false
    })
    await flushPromises()

    expect(wrapper.vm.selectedProductRecipe).toHaveLength(1)
    expect(wrapper.vm.recipeText).toBe('PP-ARCH — 100 %')
    expect(document.body.textContent).toContain('Рецепт изделия')
    expect(document.body.textContent).toContain('PP-ARCH — 100 %')
  })
})
