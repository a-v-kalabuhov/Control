// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import TaskDetailModal from '../TaskDetailModal.vue'

function mountModal(task) {
  setActivePinia(createPinia())
  return mount(TaskDetailModal, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('TaskDetailModal — рабочий режим', () => {
  // Находка 4: до загрузки задания (task === null) строка «Рабочий режим»
  // должна показывать прочерк, как соседние строки, а не «Автомат» по умолчанию.
  it('без задания показывает прочерк, а не «Автомат»', async () => {
    const wrapper = mountModal(null)
    await flushPromises()
    expect(wrapper.text()).toContain('—')
    expect(wrapper.text()).not.toContain('Автомат')
  })

  it('workMode отсутствует в загруженном задании → прочерк', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Draft' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('—')
  })

  it('workMode: Auto → «Автомат»', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Draft', workMode: 'Auto' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('Автомат')
  })

  it('workMode: SemiAuto → «Полуавтомат»', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Draft', workMode: 'SemiAuto' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('Полуавтомат')
  })
})
