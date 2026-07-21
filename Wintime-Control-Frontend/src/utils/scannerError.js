/**
 * Приводит произвольный throwable к читаемому тексту.
 *
 * html5-qrcode бросает строки (напр. "HTML Element with id=... not found"),
 * а браузерные API камеры — DOMException/Error с .message. Прямое `error.message`
 * на строке даёт `undefined`, из-за чего пользователь видел «undefined».
 */
export function scannerErrorText(error) {
  if (error == null) return 'неизвестная ошибка'
  if (typeof error === 'string') return error
  if (error.message) return error.message
  return String(error)
}
