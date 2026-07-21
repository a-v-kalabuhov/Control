import { describe, it, expect } from 'vitest'
import { scannerErrorText } from '../scannerError'

describe('scannerErrorText', () => {
  it('возвращает строку как есть (html5-qrcode бросает строки)', () => {
    expect(scannerErrorText('HTML Element with id=qr-reader not found'))
      .toBe('HTML Element with id=qr-reader not found')
  })

  it('берёт .message у Error/DOMException', () => {
    expect(scannerErrorText(new Error('Permission denied'))).toBe('Permission denied')
  })

  it('приводит объект без .message через String()', () => {
    expect(scannerErrorText({ code: 42 })).toBe('[object Object]')
  })

  it('даёт запасной текст для null/undefined вместо «undefined»', () => {
    expect(scannerErrorText(null)).toBe('неизвестная ошибка')
    expect(scannerErrorText(undefined)).toBe('неизвестная ошибка')
  })
})
