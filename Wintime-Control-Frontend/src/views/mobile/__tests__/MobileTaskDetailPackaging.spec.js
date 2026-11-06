// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import MobileTaskDetailView from '../MobileTaskDetailView.vue'

vi.mock('@/api/mobile', () => ({ mobileApi: {} }))

describe('MobileTaskDetailView — тарная единица (PZP-29)', () => {
  it('показывает тарную единицу в карточке задания', async () => {
    setActivePinia(createPinia())
    const wrapper = mount(MobileTaskDetailView, {
      props: { modelValue: true, task: { id: 't1', status: 'Planned', packagingUnit: 24 } },
      global: { plugins: [ElementPlus] },
      attachTo: document.body
    })
    await flushPromises()

    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Тарная единица')
    expect(cells[idx + 1].text()).toBe('24 шт.')
    wrapper.unmount()
  })
})
