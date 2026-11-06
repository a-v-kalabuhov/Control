function csvCell(value) {
  let text = String(value ?? '')
  if (/^\s*[=+\-@]/.test(text)) text = `'${text}`
  return /[;"\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

export function buildNomenclatureReportCsv(report) {
  const rows = [
    ['Строка', 'Артикул', 'Тип', 'Сообщение'],
    ...(report ?? []).map(item => [
      item.rowNumber,
      item.fullArticle,
      item.blocksRow ? 'Ошибка' : 'Замечание',
      item.message
    ])
  ]
  return '﻿' + rows.map(row => row.map(csvCell).join(';')).join('\r\n')
}

export function downloadNomenclatureReportCsv(report) {
  const blob = new Blob([buildNomenclatureReportCsv(report)], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `nomenclature-import-report-${new Date().toISOString().slice(0, 10)}.csv`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
