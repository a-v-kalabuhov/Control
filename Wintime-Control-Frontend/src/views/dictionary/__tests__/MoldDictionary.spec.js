// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import MoldDictionary from '../MoldDictionary.vue'

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
    getList: vi.fn(() => Promise.resolve({ data: [{ id: 'pt-1', article: 'ART', name: 'Изделие' }] }))
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
  return mount(MoldDictionary, { global: { plugins: [ElementPlus] } })
}

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
