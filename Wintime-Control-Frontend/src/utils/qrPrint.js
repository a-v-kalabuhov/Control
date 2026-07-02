function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * Собирает самодостаточный HTML-документ для окна печати QR-кода.
 * Документ сам вызывает window.print() при загрузке.
 * qrSvg — доверенная SVG-разметка, сгенерированная пакетом qrcode из нашего
 * payload (entity+id), инлайнится как есть; label — пользовательский текст, экранируется.
 */
export function buildQrPrintHtml(qrSvg, label) {
  const safeLabel = escapeHtml(label)
  return `<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>QR</title>
<style>
  html, body { margin: 0; padding: 0; }
  .wrap { display: flex; flex-direction: column; align-items: center;
          justify-content: center; min-height: 100vh; font-family: sans-serif; }
  .wrap svg { width: 60mm; height: 60mm; }
  .label { margin-top: 4mm; font-size: 14pt; font-weight: 600; text-align: center; }
  @media print { .wrap { min-height: auto; padding-top: 10mm; } }
</style></head>
<body onload="window.focus(); window.print();">
  <div class="wrap">
    ${qrSvg}
    <div class="label">${safeLabel}</div>
  </div>
</body></html>`
}
