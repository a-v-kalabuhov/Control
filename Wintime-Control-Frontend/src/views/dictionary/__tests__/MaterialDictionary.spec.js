// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage, ElMessageBox } from 'element-plus'
import MaterialDictionary from '../MaterialDictionary.vue'

vi.mock('@/api/materials', () => ({
  materialsApi: {
    getList: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(() => Promise.resolve({ data: {} })),
    update: vi.fn(() => Promise.resolve({})),
    remove: vi.fn(() => Promise.resolve({})),
    suggestManufacturers: vi.fn(() => Promise.resolve({ data: [] })),
    suggestColors: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

import { materialsApi } from '@/api/materials'

const plastic = { id: 'm1', type: 'Plastic', article: 'PP-01', name: 'Полипропилен', manufacturer: 'Сибур',
  colorName: null, colorCode: null, comment: null, isActive: true, isUsed: false }
const usedPlastic = { ...plastic, id: 'm2', article: 'PP-02', name: 'ПП-2', isUsed: true }

const mounted = []
function mountView(materialType = 'Plastic') {
  const w = mount(MaterialDictionary, {
    props: { materialType },
    global: { plugins: [ElementPlus], stubs: { Plus: true } },
    attachTo: document.body
  })
  mounted.push(w)
  return w
}

describe('MaterialDictionary (PZP-22)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    materialsApi.getList.mockResolvedValue({ data: [plastic, usedPlastic] })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('пластики: заголовок, кнопка и запрос списка по типу', async () => {
    const w = mountView('Plastic')
    await flushPromises()
    expect(w.text()).toContain('Справочник пластиков')
    expect(w.text()).toContain('Добавить пластик')
    expect(materialsApi.getList).toHaveBeenCalledWith(expect.objectContaining({ type: 'Plastic' }))
  })

  it('красители: свой заголовок и тип в запросе', async () => {
    const w = mountView('Colorant')
    await flushPromises()
    expect(w.text()).toContain('Справочник красителей')
    expect(materialsApi.getList).toHaveBeenCalledWith(expect.objectContaining({ type: 'Colorant' }))
  })

  it('кнопка «Удалить» только у неиспользованных', async () => {
    const w = mountView()
    await flushPromises()
    expect(w.findAll('.material-delete')).toHaveLength(1)
  })

  it('артикул переводится в верхний регистр на лету', async () => {
    const w = mountView()
    await flushPromises()
    w.vm.showCreateModal()
    await flushPromises()
    w.vm.form.article = 'pp-03'
    w.vm.onArticleInput()
    expect(w.vm.form.article).toBe('PP-03')
  })

  it('краситель без цвета не сохраняется', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const w = mountView('Colorant')
    await flushPromises()
    w.vm.showCreateModal()
    Object.assign(w.vm.form, { article: 'C-01', name: 'Краситель', colorName: '' })
    await w.vm.save()
    expect(materialsApi.create).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledWith('Для красителя укажите цвет')
  })

  it('пластик без цвета сохраняется с типом и нормализованным артикулом', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    const w = mountView('Plastic')
    await flushPromises()
    w.vm.showCreateModal()
    Object.assign(w.vm.form, { article: ' pp-09 ', name: ' Полиэтилен ', manufacturer: 'Сибур' })
    await w.vm.save()
    expect(materialsApi.create).toHaveBeenCalledWith(expect.objectContaining({
      type: 'Plastic', article: 'PP-09', name: 'Полиэтилен', manufacturer: 'Сибур'
    }))
  })

  it('правка отправляет пустые строки для очищенных полей и не отправляет тип', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    const w = mountView('Plastic')
    await flushPromises()
    w.vm.editItem(plastic)
    w.vm.form.manufacturer = ''
    await w.vm.save()
    const [id, payload] = materialsApi.update.mock.calls[0]
    expect(id).toBe('m1')
    expect(payload.manufacturer).toBe('')
    expect(payload).not.toHaveProperty('type')
  })

  it('выбор цвета из подсказки заполняет наименование и код', async () => {
    const w = mountView('Colorant')
    await flushPromises()
    w.vm.showCreateModal()
    w.vm.onColorSelect({ name: 'Чёрный', code: 'RAL 9005' })
    expect(w.vm.form.colorName).toBe('Чёрный')
    expect(w.vm.form.colorCode).toBe('RAL 9005')
  })

  it('архив отправляет isActive=false через update', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    const w = mountView()
    await flushPromises()
    await w.vm.toggleArchive(plastic)
    expect(materialsApi.update).toHaveBeenCalledWith('m1', { isActive: false })
  })

  it('удаление после подтверждения', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const w = mountView()
    await flushPromises()
    await w.vm.deleteItem(plastic)
    expect(materialsApi.remove).toHaveBeenCalledWith('m1')
  })
})
