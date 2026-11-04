// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import TaskDetailModal from '../TaskDetailModal.vue'

vi.mock('@/api/materials', () => ({
  materialsApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) }
}))
vi.mock('@/api/tasks', () => ({
  tasksApi: {
    replaceRecipeIngredient: vi.fn(() => Promise.resolve({ data: { id: 'task-1', recipeIngredients: [], recipeEdited: true } })),
    getResults: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))

const recipe = [
  {
    id: 'ri-1', materialId: 'pp-2', materialType: 'Plastic',
    materialArticle: 'PP-2', materialName: 'Полипропилен 2',
    originalMaterialId: 'pp-1', originalMaterialArticle: 'PP-1', originalMaterialName: 'Полипропилен',
    percent: 100
  }
]

function mountModal(task, role = 'Manager') {
  setActivePinia(createPinia())
  useAuthStore().userRole = role
  return mount(TaskDetailModal, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('TaskDetailModal — рецепт (BL-36)', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => { document.body.innerHTML = '' })

  it('показывает состав рецепта и «было: …» для заменённой строки', async () => {
    mountModal({ id: 'task-1', status: 'Planned', recipeIngredients: recipe, recipeEdited: true })
    await flushPromises()

    expect(document.body.textContent).toContain('Рецепт')
    expect(document.body.textContent).toContain('PP-2 — Полипропилен 2')
    expect(document.body.textContent).toContain('было: PP-1 — Полипропилен')
  })

  it('пустая копия — «Рецепт не задан»', async () => {
    mountModal({ id: 'task-1', status: 'Planned', recipeIngredients: [], recipeEdited: false })
    await flushPromises()

    expect(document.body.textContent).toContain('Рецепт не задан')
  })

  it('менеджер видит «Заменить», наладчик — нет', async () => {
    const manager = mountModal({ id: 'task-1', status: 'Planned', recipeIngredients: recipe, recipeEdited: false })
    await flushPromises()
    expect(document.body.querySelectorAll('.recipe-replace')).toHaveLength(1)

    manager.unmount()
    document.body.innerHTML = ''
    mountModal({ id: 'task-1', status: 'Planned', recipeIngredients: recipe, recipeEdited: false }, 'Adjuster')
    await flushPromises()
    expect(document.body.querySelectorAll('.recipe-replace')).toHaveLength(0)
  })

  it('readonly-карточка (из хронологии ТПА) без кнопок замены', async () => {
    setActivePinia(createPinia())
    useAuthStore().userRole = 'Manager'
    mount(TaskDetailModal, {
      props: { modelValue: true, task: { id: 'task-1', status: 'Planned', recipeIngredients: recipe }, readonly: true },
      global: { plugins: [ElementPlus] },
      attachTo: document.body
    })
    await flushPromises()

    expect(document.body.querySelectorAll('.recipe-replace')).toHaveLength(0)
  })

  it('сумма пластиков ≠ 100 % выделена красным', async () => {
    const partial = [{ ...recipe[0], percent: 98 }]
    mountModal({ id: 'task-1', status: 'Planned', recipeIngredients: partial, recipeEdited: false })
    await flushPromises()

    expect(document.body.textContent).toContain('98 %')
    expect(document.body.querySelector('.recipe-sum').classList).toContain('text-red-600')
  })
})
