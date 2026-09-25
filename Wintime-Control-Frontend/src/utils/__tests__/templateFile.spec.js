import { describe, it, expect } from 'vitest'
import {
  TEMPLATE_FILE_FORMAT,
  TEMPLATE_FILE_VERSION,
  buildTemplateExport,
  templateExportFileName,
  parseTemplateImport,
  formToRequest
} from '../templateFile'

const NOW = new Date('2026-09-25T10:00:00.000Z')

const fullTemplate = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Haitian MA1200 (цех 2)',
  manufacturer: 'Haitian',
  model: 'MA1200',
  version: '2.1',
  author: 'Wintime',
  connectorType: 'usr-modbus',
  jsonConfig: '{"sensors":[{"name":"Температура зоны 1","field":"temp_zone_1","type":"float","threshold":0.5}]}',
  createdAt: '2026-09-01T08:00:00Z',
  updatedAt: '2026-09-02T08:00:00Z'
}

const sparseTemplate = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Минимальный',
  manufacturer: null,
  model: '',
  version: '',
  author: null,
  connectorType: '',
  jsonConfig: '{}'
}

const numericTemplate = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Числа и кириллица',
  manufacturer: 'Siger',
  model: '160v5',
  version: '1.0',
  author: 'Технолог',
  connectorType: null,
  jsonConfig: '{"a":0.5,"b":1.5,"nested":{"arr":[1,2.25,-3]},"label":"Давление, бар"}'
}

// Имитация TemplatesController.Create: строки ?? "", version ?? "1.0",
// jsonConfig сериализуется обратно в строку.
function simulateServerCreate(request) {
  return {
    id: '99999999-9999-9999-9999-999999999999',
    name: request.name,
    manufacturer: request.manufacturer ?? '',
    model: request.model ?? '',
    version: request.version ?? '1.0',
    author: request.author ?? '',
    connectorType: request.connectorType,
    jsonConfig: JSON.stringify(request.jsonConfig)
  }
}

function withoutExportedAt(file) {
  const { exportedAt, ...rest } = file
  return rest
}

describe('buildTemplateExport', () => {
  it('собирает файл формата v1 с полями панели', () => {
    const file = buildTemplateExport(fullTemplate, NOW)
    expect(file).toEqual({
      format: TEMPLATE_FILE_FORMAT,
      formatVersion: TEMPLATE_FILE_VERSION,
      exportedAt: '2026-09-25T10:00:00.000Z',
      template: {
        name: 'Haitian MA1200 (цех 2)',
        manufacturer: 'Haitian',
        model: 'MA1200',
        version: '2.1',
        author: 'Wintime',
        connectorType: 'usr-modbus',
        jsonConfig: {
          sensors: [{ name: 'Температура зоны 1', field: 'temp_zone_1', type: 'float', threshold: 0.5 }]
        }
      }
    })
  })

  it('не экспортирует служебные поля', () => {
    const { template } = buildTemplateExport(fullTemplate, NOW)
    expect(template).not.toHaveProperty('id')
    expect(template).not.toHaveProperty('createdAt')
    expect(template).not.toHaveProperty('updatedAt')
    expect(template).not.toHaveProperty('isActive')
  })

  it('нормализует пустые поля', () => {
    const { template } = buildTemplateExport(sparseTemplate, NOW)
    expect(template.manufacturer).toBe('')
    expect(template.model).toBe('')
    expect(template.author).toBe('')
    expect(template.version).toBe('1.0')
    expect(template.connectorType).toBeNull()
    expect(template.jsonConfig).toEqual({})
  })

  it('бросает ошибку на некорректном jsonConfig', () => {
    expect(() => buildTemplateExport({ ...fullTemplate, jsonConfig: '{oops' }, NOW))
      .toThrow('Шаблон содержит некорректную JSON-конфигурацию')
  })
})

describe('templateExportFileName', () => {
  it('строит имя из наименования и версии', () => {
    expect(templateExportFileName({ name: 'Haitian', version: '2.1' })).toBe('template-Haitian-2.1.json')
  })

  it('заменяет недопустимые и пробельные символы на _', () => {
    expect(templateExportFileName({ name: 'A/B\\C:D*E?F"G<H>I|J K', version: '1.0' }))
      .toBe('template-A_B_C_D_E_F_G_H_I_J_K-1.0.json')
  })

  it('подставляет версию 1.0, если она пустая', () => {
    expect(templateExportFileName({ name: 'X', version: '' })).toBe('template-X-1.0.json')
  })
})

describe('parseTemplateImport', () => {
  const validFile = (templatePatch = {}, filePatch = {}) => JSON.stringify({
    format: TEMPLATE_FILE_FORMAT,
    formatVersion: 1,
    exportedAt: '2026-09-25T10:00:00.000Z',
    template: {
      name: 'Импорт',
      manufacturer: 'Haitian',
      model: 'MA1200',
      version: '2.1',
      author: 'Wintime',
      connectorType: 'usr-modbus',
      jsonConfig: { sensors: [] },
      ...templatePatch
    },
    ...filePatch
  })

  it('возвращает поля формы', () => {
    expect(parseTemplateImport(validFile())).toEqual({
      name: 'Импорт',
      manufacturer: 'Haitian',
      model: 'MA1200',
      version: '2.1',
      author: 'Wintime',
      connectorType: 'usr-modbus',
      jsonConfigString: JSON.stringify({ sensors: [] }, null, 2)
    })
  })

  it('подставляет умолчания для отсутствующих необязательных полей', () => {
    const text = JSON.stringify({ format: TEMPLATE_FILE_FORMAT, formatVersion: 1, template: { name: 'Только имя' } })
    expect(parseTemplateImport(text)).toEqual({
      name: 'Только имя',
      manufacturer: '',
      model: '',
      version: '1.0',
      author: '',
      connectorType: '',
      jsonConfigString: '{}'
    })
  })

  it('connectorType null → пустая строка формы', () => {
    expect(parseTemplateImport(validFile({ connectorType: null })).connectorType).toBe('')
  })

  it('ошибка: не JSON', () => {
    expect(() => parseTemplateImport('not json')).toThrow('Файл не является корректным JSON')
  })

  it('ошибка: чужой формат', () => {
    expect(() => parseTemplateImport(validFile({}, { format: 'something-else' })))
      .toThrow('Файл не является шаблоном Wintime Control')
    expect(() => parseTemplateImport('[1,2,3]')).toThrow('Файл не является шаблоном Wintime Control')
  })

  it('ошибка: неподдерживаемая версия формата', () => {
    expect(() => parseTemplateImport(validFile({}, { formatVersion: 2 })))
      .toThrow('Версия формата 2 не поддерживается')
    expect(() => parseTemplateImport(validFile({}, { formatVersion: 0 })))
      .toThrow('Версия формата 0 не поддерживается')
  })

  it('ошибка: нет наименования', () => {
    expect(() => parseTemplateImport(validFile({ name: '   ' }))).toThrow('В файле нет наименования шаблона')
    expect(() => parseTemplateImport(validFile({}, { template: null }))).toThrow('В файле нет наименования шаблона')
  })

  it('ошибка: jsonConfig не объект', () => {
    expect(() => parseTemplateImport(validFile({ jsonConfig: '{"a":1}' })))
      .toThrow('JSON-конфигурация в файле должна быть объектом')
    expect(() => parseTemplateImport(validFile({ jsonConfig: [1, 2] })))
      .toThrow('JSON-конфигурация в файле должна быть объектом')
  })
})

describe('formToRequest', () => {
  const form = {
    name: 'Ф', manufacturer: 'M', model: 'X', version: '1.0', author: 'A',
    connectorType: '', jsonConfigString: '{\n  "a": 1\n}'
  }

  it('пустой connectorType → null, jsonConfig разобран', () => {
    expect(formToRequest(form)).toEqual({
      name: 'Ф', manufacturer: 'M', model: 'X', version: '1.0', author: 'A',
      connectorType: null, jsonConfig: { a: 1 }
    })
  })

  it('пустая JSON-конфигурация → {}', () => {
    expect(formToRequest({ ...form, jsonConfigString: '' }).jsonConfig).toEqual({})
  })

  it('ошибка на битом JSON', () => {
    expect(() => formToRequest({ ...form, jsonConfigString: '{oops' }))
      .toThrow('Неверный формат JSON-конфигурации')
  })
})

describe('круговой тест: экспорт → импорт → сохранение → экспорт', () => {
  it.each([
    ['полный шаблон', fullTemplate],
    ['пустые необязательные поля', sparseTemplate],
    ['кириллица и дробные числа', numericTemplate]
  ])('%s: повторный экспорт совпадает с первым (кроме exportedAt)', (_, template) => {
    const first = buildTemplateExport(template, NOW)
    const form = parseTemplateImport(JSON.stringify(first, null, 2))
    const saved = simulateServerCreate(formToRequest(form))
    const second = buildTemplateExport(saved, new Date('2026-09-26T00:00:00.000Z'))

    expect(withoutExportedAt(second)).toEqual(withoutExportedAt(first))
  })
})
