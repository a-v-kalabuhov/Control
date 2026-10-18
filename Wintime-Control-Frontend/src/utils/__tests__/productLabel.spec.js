import { describe, it, expect } from 'vitest'
import { productSecondary, activeProducts } from '../productLabel'

describe('productLabel', () => {
  it('productSecondary склеивает тип и изделие', () => {
    expect(productSecondary('Крышка', 'Чёрный')).toBe('Крышка · Чёрный')
  })
  it('productSecondary пропускает пустые части', () => {
    expect(productSecondary(null, 'Чёрный')).toBe('Чёрный')
  })
  it('activeProducts оставляет только активные', () => {
    expect(activeProducts({ products: [{ isActive: true }, { isActive: false }] }).length).toBe(1)
  })
  it('activeProducts без типа даёт []', () => {
    expect(activeProducts(null)).toEqual([])
  })
})
