import { describe, it, expect } from 'vitest'
import {
  plasticSum,
  isRecipeSumOk,
  formatRecipe,
  formatPercent,
  recipeSumHint,
  materialTypeLabel
} from '../recipe'

const rows = [
  { materialType: 'Plastic', materialArticle: 'ПП-1', materialName: 'Полипропилен 1', percent: 60 },
  { materialType: 'Plastic', materialArticle: 'ПП-2', materialName: 'Полипропилен 2', percent: 40 },
  { materialType: 'Colorant', materialArticle: 'КРАС-1', materialName: 'Краситель', percent: 2 }
]

describe('utils/recipe', () => {
  it('plasticSum считает только пластики', () => {
    expect(plasticSum(rows)).toBe(100)
    expect(plasticSum([{ materialType: 'Colorant', percent: 2 }])).toBe(0)
  })

  it('plasticSum пустого рецепта — 0', () => {
    expect(plasticSum([])).toBe(0)
    expect(plasticSum(null)).toBe(0)
  })

  it('isRecipeSumOk: пустой рецепт — ок', () => {
    expect(isRecipeSumOk([])).toBe(true)
    expect(isRecipeSumOk(null)).toBe(true)
  })

  it('isRecipeSumOk: 100 % пластиков — ок (краситель не считается)', () => {
    expect(isRecipeSumOk(rows)).toBe(true)
  })

  it('isRecipeSumOk: 98 % пластиков — не ок', () => {
    expect(isRecipeSumOk([
      { materialType: 'Plastic', percent: 98 },
      { materialType: 'Colorant', percent: 2 }
    ])).toBe(false)
  })

  it('isRecipeSumOk: дробные проценты не ломаются на погрешности float', () => {
    expect(isRecipeSumOk([
      { materialType: 'Plastic', percent: 33.33 },
      { materialType: 'Plastic', percent: 33.33 },
      { materialType: 'Plastic', percent: 33.34 }
    ])).toBe(true)
  })

  it('formatRecipe — порядок рецепта и разделитель «; »', () => {
    expect(formatRecipe(rows)).toBe('ПП-1 — 60 %; ПП-2 — 40 %; КРАС-1 — 2 %')
    expect(formatRecipe([])).toBe('')
    expect(formatRecipe(null)).toBe('')
  })

  it('formatPercent без лишних нулей', () => {
    expect(formatPercent(60)).toBe('60')
    expect(formatPercent(2.5)).toBe('2.5')
    expect(formatPercent(0.01)).toBe('0.01')
    expect(formatPercent(33.30)).toBe('33.3')
    expect(formatPercent(null)).toBe('')
  })

  it('recipeSumHint называет сумму пластиков', () => {
    expect(recipeSumHint([{ materialType: 'Plastic', percent: 98 }]))
      .toBe('Сумма пластиков: 98 % (должно быть 100 %)')
  })

  it('materialTypeLabel переводит тип материала', () => {
    expect(materialTypeLabel('Plastic')).toBe('Пластик')
    expect(materialTypeLabel('Colorant')).toBe('Краситель')
    expect(materialTypeLabel(undefined)).toBe('')
  })
})
