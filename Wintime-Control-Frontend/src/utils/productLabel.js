// Подпись изделия (PZP-19): основная строка — полный артикул, вторичная — тип · изделие.
export function productSecondary(typeName, productName) {
  return [typeName, productName].filter(Boolean).join(' · ')
}

export function activeProducts(productType) {
  return (productType?.products ?? []).filter(p => p.isActive)
}
