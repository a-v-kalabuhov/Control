// DomainException (400) возвращается бэкендом как { message: "..." } (DomainExceptionHandler),
// а обычный BadRequest("строка") — как строка. Извлекаем читаемый текст для обоих случаев,
// иначе ElMessage.error(error.response?.data) рендерит "[object Object]" для DomainException.
export function apiErrorMessage(error, fallback) {
  return error?.response?.data?.message ?? error?.response?.data ?? fallback
}
