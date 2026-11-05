// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import MobileTaskDetailView from '../MobileTaskDetailView.vue'

vi.mock('@/api/mobile', () => ({ mobileApi: {} }))

function mountView(task) {
  setActivePinia(createPinia())
  return mount(MobileTaskDetailView, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

function cellText(wrapper, label) {
  const cells = wrapper.findAll('.el-descriptions__cell')
  const idx = cells.findIndex(c => c.text() === label)
  return idx >= 0 ? cells[idx + 1].text() : undefined
}

describe('MobileTaskDetailView — заказчик (PZP-23)', () => {
  it('без заказа показывает компанию типа изделия', async () => {
    const wrapper = mountView({ id: 't1', status: 'Planned', orderId: null, productTypeCompany: 'ООО Компания' })
    await flushPromises()

    expect(cellText(wrapper, 'Заказчик')).toBe('ООО Компания')
  })

  it('при расхождении показывает заказчика заказа и предупреждение', async () => {
    const wrapper = mountView({
      id: 't1', status: 'Planned', orderId: 'o1', orderCustomer: 'ООО Заказ', productTypeCompany: 'ООО Тип'
    })
    await flushPromises()

    expect(cellText(wrapper, 'Заказчик')).toBe('ООО Заказ')
    expect(wrapper.find('[data-test="customer-mismatch"]').exists()).toBe(true)
  })
})
