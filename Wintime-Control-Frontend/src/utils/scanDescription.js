import { immApi } from '@/api/imm'
import { moldsApi } from '@/api/molds'

/**
 * Разбирает отсканированный QR в структурированное описание.
 *
 * В самом QR лежит только { entity, id } — наименование/артикул тянем из
 * справочника по id. Возвращает:
 *   { ok, typeLabel, details, text }
 * ok       — распознана ли доменная сущность (ТПА/пресс-форма);
 * typeLabel— «ТПА» | «Пресс-форма» | …
 * details  — «<Наименование>» | «<Наименование> / <Артикул>»;
 * text     — готовая строка для показа целиком.
 */
export async function resolveScan(qrData) {
  let parsed
  try {
    parsed = JSON.parse(qrData)
  } catch {
    return { ok: false, typeLabel: 'QR', details: '', text: 'Неверный формат QR-кода' }
  }

  const { entity, id } = parsed
  try {
    if (entity === 'machine') {
      const { data } = await immApi.getById(id)
      return { ok: true, typeLabel: 'ТПА', details: data.name, text: `ТПА: ${data.name}` }
    }
    if (entity === 'mold') {
      const { data } = await moldsApi.getById(id)
      const details = `${data.name} / ${data.formId}`
      return { ok: true, typeLabel: 'Пресс-форма', details, text: `Пресс-форма: ${details}` }
    }
    return { ok: false, typeLabel: 'Объект', details: String(id ?? qrData), text: `Объект: ${id ?? qrData}` }
  } catch {
    return { ok: false, typeLabel: '—', details: '', text: 'Объект не найден в системе' }
  }
}

/**
 * Человекочитаемое описание одной строкой (для блока «QR распознан»).
 */
export async function describeScan(qrData) {
  return (await resolveScan(qrData)).text
}
