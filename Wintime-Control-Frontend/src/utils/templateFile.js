// Формат файла шаблона оборудования (импорт/экспорт между инсталляциями Control).
// Спека: docs/superpowers/specs/2026-09-25-template-import-export-design.md

export const TEMPLATE_FILE_FORMAT = 'wintime-control-template'
export const TEMPLATE_FILE_VERSION = 1

const DEFAULT_VERSION = '1.0'
const FORBIDDEN_FILENAME_CHARS = '\\/:*?"<>|'

const isPlainObject = (value) =>
  value !== null && typeof value === 'object' && !Array.isArray(value)

const asString = (value) => (typeof value === 'string' ? value : '')

/** Файл экспорта из DTO шаблона (jsonConfig в DTO — строка). */
export function buildTemplateExport(template, now = new Date()) {
  let jsonConfig
  try {
    jsonConfig = template.jsonConfig ? JSON.parse(template.jsonConfig) : {}
  } catch {
    throw new Error('Шаблон содержит некорректную JSON-конфигурацию')
  }

  if (!isPlainObject(jsonConfig)) {
    throw new Error('Шаблон содержит некорректную JSON-конфигурацию')
  }

  return {
    format: TEMPLATE_FILE_FORMAT,
    formatVersion: TEMPLATE_FILE_VERSION,
    exportedAt: now.toISOString(),
    template: {
      name: template.name ?? '',
      manufacturer: template.manufacturer ?? '',
      model: template.model ?? '',
      version: template.version || DEFAULT_VERSION,
      author: template.author ?? '',
      connectorType: template.connectorType || null,
      jsonConfig
    }
  }
}

function sanitizeFileNamePart(value) {
  return [...String(value)]
    .map((ch) => (ch < ' ' || /\s/.test(ch) || FORBIDDEN_FILENAME_CHARS.includes(ch) ? '_' : ch))
    .join('')
}

export function templateExportFileName(template) {
  const name = sanitizeFileNamePart(template.name ?? '')
  const version = sanitizeFileNamePart(template.version || DEFAULT_VERSION)
  return `template-${name}-${version}.json`
}

/** Текст файла → поля формы шаблона. Бросает Error с сообщением для пользователя. */
export function parseTemplateImport(text) {
  let data
  try {
    data = JSON.parse(text)
  } catch {
    throw new Error('Файл не является корректным JSON')
  }

  if (!isPlainObject(data) || data.format !== TEMPLATE_FILE_FORMAT) {
    throw new Error('Файл не является шаблоном Wintime Control')
  }

  const formatVersion = data.formatVersion
  if (formatVersion === undefined) {
    throw new Error('В файле не указана версия формата')
  }
  // Принимаем версии 1..TEMPLATE_FILE_VERSION: старые файлы должны импортироваться и после выхода новых версий формата.
  if (!Number.isInteger(formatVersion) || formatVersion < 1 || formatVersion > TEMPLATE_FILE_VERSION) {
    throw new Error(`Версия формата ${formatVersion} не поддерживается`)
  }

  const t = data.template
  if (!isPlainObject(t) || typeof t.name !== 'string' || !t.name.trim()) {
    throw new Error('В файле нет наименования шаблона')
  }

  if (t.jsonConfig !== undefined && t.jsonConfig !== null && !isPlainObject(t.jsonConfig)) {
    throw new Error('JSON-конфигурация в файле должна быть объектом')
  }

  return {
    name: t.name,
    manufacturer: asString(t.manufacturer),
    model: asString(t.model),
    version: asString(t.version) || DEFAULT_VERSION,
    author: asString(t.author),
    connectorType: asString(t.connectorType),
    jsonConfigString: JSON.stringify(t.jsonConfig ?? {}, null, 2)
  }
}

/** Поля формы → тело запроса create/update. */
export function formToRequest(form) {
  let jsonConfig
  try {
    jsonConfig = form.jsonConfigString ? JSON.parse(form.jsonConfigString) : {}
  } catch {
    throw new Error('Неверный формат JSON-конфигурации')
  }

  return {
    name: form.name,
    manufacturer: form.manufacturer,
    model: form.model,
    version: form.version,
    author: form.author,
    connectorType: form.connectorType || null,
    jsonConfig
  }
}
