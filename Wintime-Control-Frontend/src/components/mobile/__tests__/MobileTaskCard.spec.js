// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import MobileTaskCard from '../MobileTaskCard.vue'

const task = {
  id: 't1', status: 'Issued', immName: 'ТПА-1', moldName: 'Форма 1',
  productFullArticle: 'A123.BLK', productTypeName: 'Крышка', productName: 'Чёрный',
  planQuantity: 100, actualQuantity: 0, plannedStartAt: '2026-10-14T05:00:00Z', plannedDate: '2026-10-14'
}

describe('MobileTaskCard — изделие (PZP-19)', () => {
  it('показывает полный артикул и вторичную подпись', () => {
    const w = mount(MobileTaskCard, { props: { task }, global: { plugins: [ElementPlus] } })
    expect(w.text()).toContain('A123.BLK')
    expect(w.text()).toContain('Крышка · Чёрный')
  })
})
