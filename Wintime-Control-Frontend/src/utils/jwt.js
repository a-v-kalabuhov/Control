// Клейм sub (= User.Id, см. JwtTokenService) из JWT без проверки подписи — только для
// клиентских нужд (ключ локальных настроек), не для авторизации.
export function jwtSubject(token) {
  if (typeof token !== 'string') return null
  const parts = token.split('.')
  if (parts.length !== 3) return null
  try {
    const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/')
    const payload = JSON.parse(atob(b64.padEnd(b64.length + ((4 - (b64.length % 4)) % 4), '=')))
    return typeof payload?.sub === 'string' && payload.sub ? payload.sub : null
  } catch {
    return null
  }
}
