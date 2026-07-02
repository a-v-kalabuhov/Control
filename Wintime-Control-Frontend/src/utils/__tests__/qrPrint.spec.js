import { describe, it, expect } from 'vitest'
import { buildQrPrintHtml } from '@/utils/qrPrint'

const SVG = '<svg data-qr="1"><path d="M0 0h1v1H0z"/></svg>'

describe('buildQrPrintHtml', () => {
  it('инлайнит переданную SVG-разметку QR', () => {
    const html = buildQrPrintHtml(SVG, 'ТПА-05')
    expect(html).toContain(SVG)
  })

  it('печатает подпись и вызывает печать при загрузке', () => {
    const html = buildQrPrintHtml(SVG, 'ТПА-05')
    expect(html).toContain('ТПА-05')
    expect(html).toContain('window.print()')
  })

  it('экранирует угловые скобки в подписи (label — не доверенный текст)', () => {
    const html = buildQrPrintHtml(SVG, '<script>x</script>')
    expect(html).not.toContain('<script>x')
    expect(html).toContain('&lt;script&gt;')
  })
})
