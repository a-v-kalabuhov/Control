// Рецепт (BL-36): проценты от массы изделия. В 100 % входят только пластики —
// краситель в сумму не входит. Сумма ≠ 100 % — предупреждение в UI, не запрет.
export function isPlastic(row) {
  return row?.materialType === 'Plastic'
}

export function plasticSum(rows) {
  return (rows ?? []).filter(isPlastic).reduce((sum, row) => sum + Number(row.percent ?? 0), 0)
}

// Округление до копеек процента: 33.33 + 33.33 + 33.34 не должно «не сходиться» из-за float.
export function isRecipeSumOk(rows) {
  const list = rows ?? []
  if (list.length === 0) return true
  return Math.round(plasticSum(list) * 100) / 100 === 100
}

export function formatPercent(value) {
  if (value === null || value === undefined || value === '') return ''
  return String(Math.round(Number(value) * 100) / 100)
}

export function formatRecipe(rows) {
  const list = rows ?? []
  if (list.length === 0) return ''
  return list
    .map(row => `${row.materialArticle || row.materialName || '—'} — ${formatPercent(row.percent)} %`)
    .join('; ')
}

export function recipeSumHint(rows) {
  return `Сумма пластиков: ${formatPercent(plasticSum(rows))} % (должно быть 100 %)`
}

export function materialTypeLabel(type) {
  if (type === 'Plastic') return 'Пластик'
  if (type === 'Colorant') return 'Краситель'
  return ''
}
