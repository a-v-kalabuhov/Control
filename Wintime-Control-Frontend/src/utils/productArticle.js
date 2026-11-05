// Следующая числовая часть артикула; буквенные значения и пропуски в нумерации игнорируются.
export function nextProductArticle(products) {
  let max = -1n

  for (const product of products) {
    const article = String(product?.article ?? '').trim()
    if (!/^[0-9]+$/.test(article)) continue

    const value = BigInt(article)
    if (value > max) max = value
  }

  return (max + 1n).toString()
}
