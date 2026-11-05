import { describe, expect, it } from 'vitest'
import { nextProductArticle } from '../productArticle'

const product = (article, isActive = true) => ({ article, isActive })

describe('nextProductArticle (PZP-27)', () => {
  it('предлагает 0, если числовых артикулов нет', () => {
    expect(nextProductArticle([])).toBe('0')
    expect(nextProductArticle([product('BLK'), product('OLD', false)])).toBe('0')
  })

  it('предлагает максимальный числовой артикул плюс один без заполнения пропусков', () => {
    expect(nextProductArticle([
      product('0'), product('1'), product('4'), product('BLK')
    ])).toBe('5')
  })

  it('учитывает архивные изделия', () => {
    expect(nextProductArticle([product('0'), product('7', false)])).toBe('8')
  })

  it('сравнивает артикулы с ведущими нулями по числовому значению', () => {
    expect(nextProductArticle([product('0'), product('02'), product('10')])).toBe('11')
  })

  it('игнорирует пробелы по краям и не считает числовыми смешанные значения', () => {
    expect(nextProductArticle([
      product(' 12 '), product('-30'), product('1.5'), product('40A'), product('½'), product(null)
    ])).toBe('13')
  })

  it('не теряет точность на числовых артикулах больше Number.MAX_SAFE_INTEGER', () => {
    expect(nextProductArticle([product('9007199254740993')])).toBe('9007199254740994')
  })
})
