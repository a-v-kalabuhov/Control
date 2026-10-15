// PZP-16 (ADR-0018): совместимость ПФ↔ТПА — ручной список в карточке ПФ.
// Пустой список — совместимость не указана, ПФ подходит к любому ТПА.
export function isMoldCompatibleWithImm(mold, immId) {
  const list = mold?.compatibleImms ?? []
  return list.length === 0 || list.some(imm => imm.id === immId)
}

export function immOptionLabel(imm) {
  return imm.isActive ? imm.name : `${imm.name} (архив)`
}

export function formatCompatibleImms(list) {
  return (list ?? []).map(immOptionLabel).join(', ')
}
