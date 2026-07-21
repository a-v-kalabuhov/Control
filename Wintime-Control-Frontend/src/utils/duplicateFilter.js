/**
 * Фильтр повторов по ключу со скользящим окном.
 *
 * Сканер QR выдаёт один и тот же код много раз в секунду (fps камеры).
 * Возвращаемая функция пропускает код только если этот же код не встречался
 * в течение windowMs — так в историю попадает одна запись, а не десятки.
 *
 * @param {number} windowMs период подавления повторов одного ключа, мс
 * @param {() => number} now источник времени (инъекция для тестов)
 * @returns {(key: string) => boolean} true — ключ принят; false — свежий повтор
 */
export function createDuplicateFilter(windowMs, now = () => Date.now()) {
  const lastSeen = new Map()
  return (key) => {
    const t = now()
    const prev = lastSeen.get(key)
    if (prev !== undefined && t - prev < windowMs) return false
    lastSeen.set(key, t)
    return true
  }
}
