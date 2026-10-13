// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import MobileTaskDetailView from '../MobileTaskDetailView.vue'
import MobileTaskCard from '@/components/mobile/MobileTaskCard.vue'

const baseTask = {
  id: 't1',
  immId: 'imm-1',
  immName: 'ТПА-1',
  moldName: 'ПФ-1',
  planQuantity: 100,
  actualQuantity: 0,
  personnelName: 'Иванов',
  plannedStartAt: '2026-10-14T05:00:00Z',
  plannedDate: '2026-10-14',
  plannedSetupMinutes: 90
}

const mountDetail = (task) => mount(MobileTaskDetailView, {
  props: { modelValue: true, task },
  global: { plugins: [ElementPlus] },
  attachTo: document.body
})

const mountCard = (task) => mount(MobileTaskCard, {
  props: { task },
  global: { plugins: [ElementPlus] }
})

describe('BL-41: норматив наладки у наладчика', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
  })

  it('экран задания показывает норматив наладки', async () => {
    const wrapper = mountDetail({ ...baseTask, status: 'Issued' })
    await wrapper.vm.$nextTick()
    expect(document.body.textContent).toContain('Норматив наладки')
    expect(document.body.textContent).toContain('1 ч 30 мин')
    wrapper.unmount()
  })

  it('карточка выданного задания показывает норматив наладки', () => {
    const wrapper = mountCard({ ...baseTask, status: 'Issued' })
    expect(wrapper.text()).toContain('Наладка')
    expect(wrapper.text()).toContain('1 ч 30 мин')
  })

  it('карточка задания в наладке показывает норматив наладки', () => {
    const wrapper = mountCard({ ...baseTask, status: 'Setup' })
    expect(wrapper.text()).toContain('1 ч 30 мин')
  })

  it('карточка задания в работе норматив не показывает', () => {
    const wrapper = mountCard({ ...baseTask, status: 'InProgress', actualQuantity: 10 })
    expect(wrapper.text()).not.toContain('1 ч 30 мин')
  })
})
