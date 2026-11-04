// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ReplaceIngredientDialog from '../ReplaceIngredientDialog.vue'
import { materialsApi } from '@/api/materials'
import { tasksApi } from '@/api/tasks'

vi.mock('@/api/materials', () => ({
  materialsApi: { getList: vi.fn() }
}))
vi.mock('@/api/tasks', () => ({
  tasksApi: { replaceRecipeIngredient: vi.fn() }
}))

const deferred = () => {
  let resolve
  let reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

const material = (id, article = id.toUpperCase()) => ({
  id, article, name: `Материал ${article}`, type: 'Plastic', isActive: true
})
const ingredient = (id, materialId) => ({
  id, materialId, materialType: 'Plastic', materialArticle: materialId.toUpperCase(),
  materialName: `Материал ${materialId.toUpperCase()}`,
  originalMaterialId: materialId, originalMaterialArticle: materialId.toUpperCase(),
  originalMaterialName: `Материал ${materialId.toUpperCase()}`, percent: 50
})
const task = (id, rows) => ({ id, recipeIngredients: rows })

function mountDialog(taskValue, ingredientValue) {
  return mount(ReplaceIngredientDialog, {
    props: { modelValue: true, task: taskValue, ingredient: ingredientValue },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('ReplaceIngredientDialog — асинхронные гонки и состав списка', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => { document.body.innerHTML = '' })

  it('не предлагает материалы других строк, но оставляет текущий материал строки', async () => {
    const a = ingredient('row-a', 'a')
    const b = ingredient('row-b', 'b')
    materialsApi.getList.mockResolvedValue({ data: [material('a'), material('b'), material('c')] })
    const wrapper = mountDialog(task('task-a', [a, b]), a)

    await wrapper.vm.loadMaterials()

    expect(wrapper.vm.options.map(m => m.id)).toEqual(['a', 'c'])
  })

  it('поздний ответ загрузки не перезаписывает материалы нового ингредиента', async () => {
    const oldRequest = deferred()
    const newRequest = deferred()
    materialsApi.getList
      .mockImplementationOnce(() => oldRequest.promise)
      .mockImplementationOnce(() => newRequest.promise)
    const wrapper = mountDialog(task('task-a', []), ingredient('row-a', 'a'))

    const oldLoad = wrapper.vm.loadMaterials()
    await wrapper.setProps({ task: task('task-b', []), ingredient: ingredient('row-b', 'b') })
    const newLoad = wrapper.vm.loadMaterials()
    newRequest.resolve({ data: [material('b'), material('new-b')] })
    await newLoad
    oldRequest.resolve({ data: [material('a'), material('old-a')] })
    await oldLoad

    expect(wrapper.vm.options.map(m => m.id)).toEqual(['b', 'new-b'])
    expect(wrapper.vm.loading).toBe(false)
  })

  it('поздняя ошибка загрузки не показывает ошибку и не снимает loading нового запроса', async () => {
    const oldRequest = deferred()
    const newRequest = deferred()
    materialsApi.getList
      .mockImplementationOnce(() => oldRequest.promise)
      .mockImplementationOnce(() => newRequest.promise)
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const wrapper = mountDialog(task('task-a', []), ingredient('row-a', 'a'))

    const oldLoad = wrapper.vm.loadMaterials()
    await wrapper.setProps({ task: task('task-b', []), ingredient: ingredient('row-b', 'b') })
    const newLoad = wrapper.vm.loadMaterials()
    oldRequest.reject(new Error('stale'))
    await oldLoad

    expect(error).not.toHaveBeenCalled()
    expect(wrapper.vm.loading).toBe(true)

    newRequest.resolve({ data: [material('b')] })
    await newLoad
    expect(wrapper.vm.loading).toBe(false)
  })

  it('позднее сохранение не закрывает новый диалог и не снимает saving нового запроса', async () => {
    const oldRequest = deferred()
    const newRequest = deferred()
    tasksApi.replaceRecipeIngredient
      .mockImplementationOnce(() => oldRequest.promise)
      .mockImplementationOnce(() => newRequest.promise)
    const wrapper = mountDialog(task('task-a', []), ingredient('row-a', 'a'))

    wrapper.vm.materialId = 'new-a'
    const oldSave = wrapper.vm.save()
    await wrapper.setProps({ task: task('task-b', []), ingredient: ingredient('row-b', 'b') })
    wrapper.vm.materialId = 'new-b'
    const newSave = wrapper.vm.save()
    oldRequest.resolve({ data: { id: 'task-a' } })
    await oldSave

    expect(wrapper.emitted('update:modelValue') ?? []).not.toContainEqual([false])
    expect(wrapper.vm.saving).toBe(true)

    newRequest.resolve({ data: { id: 'task-b' } })
    await newSave
    expect(wrapper.emitted('update:modelValue')).toContainEqual([false])
    expect(wrapper.emitted('replaced').at(-1)).toEqual([{ id: 'task-b' }])
    expect(wrapper.vm.saving).toBe(false)
  })

  it('поздняя ошибка сохранения не затрагивает новый контекст', async () => {
    const oldRequest = deferred()
    tasksApi.replaceRecipeIngredient.mockImplementationOnce(() => oldRequest.promise)
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const wrapper = mountDialog(task('task-a', []), ingredient('row-a', 'a'))

    wrapper.vm.materialId = 'new-a'
    const oldSave = wrapper.vm.save()
    await wrapper.setProps({ task: task('task-b', []), ingredient: ingredient('row-b', 'b') })
    oldRequest.reject(new Error('stale'))
    await oldSave
    await flushPromises()

    expect(error).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue') ?? []).not.toContainEqual([false])
  })
})
