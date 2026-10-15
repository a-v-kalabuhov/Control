import { describe, it, expect } from 'vitest'
import { isMoldCompatibleWithImm, immOptionLabel, formatCompatibleImms } from '../moldCompatibility'

describe('isMoldCompatibleWithImm', () => {
  it('пустой список — ПФ подходит к любому ТПА', () => {
    expect(isMoldCompatibleWithImm({ compatibleImms: [] }, 'imm-1')).toBe(true)
  })
  it('без поля compatibleImms — как пустой список', () => {
    expect(isMoldCompatibleWithImm({}, 'imm-1')).toBe(true)
  })
  it('ТПА в списке — совместима', () => {
    expect(isMoldCompatibleWithImm({ compatibleImms: [{ id: 'imm-1' }] }, 'imm-1')).toBe(true)
  })
  it('ТПА не в списке — несовместима', () => {
    expect(isMoldCompatibleWithImm({ compatibleImms: [{ id: 'imm-2' }] }, 'imm-1')).toBe(false)
  })
})

describe('immOptionLabel / formatCompatibleImms', () => {
  it('архивный ТПА помечается «(архив)»', () => {
    expect(immOptionLabel({ name: 'ТПА-1', isActive: false })).toBe('ТПА-1 (архив)')
    expect(immOptionLabel({ name: 'ТПА-1', isActive: true })).toBe('ТПА-1')
  })
  it('список — имена через запятую', () => {
    expect(formatCompatibleImms([
      { name: 'ТПА-1', isActive: true },
      { name: 'ТПА-2', isActive: false }
    ])).toBe('ТПА-1, ТПА-2 (архив)')
  })
  it('пустой или отсутствующий список — пустая строка', () => {
    expect(formatCompatibleImms([])).toBe('')
    expect(formatCompatibleImms(undefined)).toBe('')
  })
})
