import { describe, it, expect } from 'vitest'
import { buildNomenclatureReportCsv } from '../nomenclatureImportReport'

describe('nomenclatureImportReport', () => {
  it('строит CSV с BOM, русскими заголовками и экранированием', () => {
    const csv = buildNomenclatureReportCsv([
      { rowNumber: 12, fullArticle: '=1+1.0', blocksRow: true, message: 'Ошибка; "важно"' },
      { rowNumber: 13, fullArticle: 'A.2', blocksRow: false, message: 'Предупреждение' }
    ])

    expect(csv.charCodeAt(0)).toBe(0xFEFF)
    expect(csv).toContain('Строка;Артикул;Тип;Сообщение')
    expect(csv).toContain("12;'=1+1.0;Ошибка;\"Ошибка; \"\"важно\"\"\"")
    expect(csv).toContain('13;A.2;Замечание;Предупреждение')
  })
})
