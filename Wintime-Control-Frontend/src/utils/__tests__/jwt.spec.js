import { describe, it, expect } from 'vitest'
import { jwtSubject } from '@/utils/jwt'

const b64url = (obj) => Buffer.from(JSON.stringify(obj)).toString('base64url')
const token = (payload) => `${b64url({ alg: 'HS256', typ: 'JWT' })}.${b64url(payload)}.sig`

describe('jwtSubject', () => {
  it('возвращает sub из payload', () => {
    expect(jwtSubject(token({ sub: 'a1b2c3d4-0000-0000-0000-000000000001', role: 'Manager' })))
      .toBe('a1b2c3d4-0000-0000-0000-000000000001')
  })

  it('декодирует base64url-символы (- и _) в payload', () => {
    // '>>>' и '???' дают в base64 символы '+' и '/', в base64url — '-' и '_'
    const t = token({ sub: 'u1', note: '>>>???' })
    expect(t.split('.')[1]).toMatch(/[-_]/)
    expect(jwtSubject(t)).toBe('u1')
  })

  it('null на пустом и мусорном токене', () => {
    expect(jwtSubject(null)).toBeNull()
    expect(jwtSubject(undefined)).toBeNull()
    expect(jwtSubject('')).toBeNull()
    expect(jwtSubject('not-a-jwt')).toBeNull()
    expect(jwtSubject('a.%%%.c')).toBeNull()
    expect(jwtSubject(`${b64url({})}.${Buffer.from('not json').toString('base64url')}.s`)).toBeNull()
  })

  it('null, если sub нет или он не строка', () => {
    expect(jwtSubject(token({ role: 'Manager' }))).toBeNull()
    expect(jwtSubject(token({ sub: 42 }))).toBeNull()
  })
})
