# Импорт/экспорт шаблонов ТПА в JSON — дизайн

Дата: 2026-09-25

## Цель

Переносить настроенные шаблоны оборудования между инсталляциями Control
(стенд → пилот у заказчика, одна площадка → другая) без ручного копирования полей.

## Акторы и сценарии (use cases)

- **Админ / интегратор — экспорт.** На странице «Шаблоны оборудования» нажимает
  «Экспорт» в строке шаблона → браузер скачивает `.json`-файл со всеми полями панели
  добавления/редактирования.
- **Админ — импорт.** Нажимает «Импорт» в шапке страницы, выбирает файл → открывается
  панель «Новый шаблон», заполненная данными из файла. Админ проверяет/правит поля и
  нажимает «Сохранить» — дальше стандартное создание шаблона (та же валидация, тот же
  `POST /api/templates`). «Отмена» — ничего не создаётся.
- **Админ — неподходящий файл.** Выбирает битый JSON / чужой файл / файл более новой
  версии формата → сообщение об ошибке с причиной, панель не открывается.

## Подход

Только фронтенд. Список шаблонов уже отдаёт `jsonConfig`, поэтому экспорт собирается
из строки таблицы; импорт — `FileReader` + существующий диалог в режиме создания.
Бэкенд-эндпоинты не вводим: сценария импорта без UI нет, а сохранение через обычный
`create` гарантирует ту же валидацию и права, что при ручном создании.

Отвергнуто: эндпоинты `GET /templates/{id}/export` / `POST /templates/import` (нет
потребителя без UI; импорт в обход панели противоречит требованию), гибрид (логика
формата в двух местах).

## Формат файла

```json
{
  "format": "wintime-control-template",
  "formatVersion": 1,
  "exportedAt": "2026-09-25T10:00:00.000Z",
  "template": {
    "name": "…",
    "manufacturer": "…",
    "model": "…",
    "version": "1.0",
    "author": "…",
    "connectorType": "usr-modbus",
    "jsonConfig": { "sensors": [ … ] }
  }
}
```

- `template` содержит ровно поля панели. `id`, `createdAt`, `isActive` не экспортируются
  (на другой инсталляции бессмысленны).
- `jsonConfig` — вложенный JSON-объект (не экранированная строка), файл читаем глазами.
  Если сохранённый `jsonConfig` не парсится как JSON или не является объектом, экспорт
  завершается ошибкой «Шаблон содержит некорректную JSON-конфигурацию» (файл не создаётся) —
  иначе такой файл потом не прошёл бы импорт.
- Нормализация при экспорте, чтобы экспорт → импорт → сохранение → экспорт давал те же данные:
  - `manufacturer`, `model`, `author`: `null` → `""` (так их хранит сервер);
  - `connectorType`: пустое значение (`""`/`null`) → `null`;
  - `version`: пустое значение → `"1.0"`.
- Файл сериализуется с отступом 2 пробела, UTF-8.
- Имя файла: `template-<name>-<version>.json`; символы `\ / : * ? " < > |` и управляющие
  заменяются на `_`, пробелы — на `_`.

## Компоненты

### `src/utils/templateFile.js` (новый, чистые функции без Vue)

- `buildTemplateExport(template, now = new Date())` → объект формата выше.
  `template` — DTO из API (`jsonConfig` — строка).
- `templateExportFileName(template)` → имя файла.
- `parseTemplateImport(text)` → поля формы
  `{ name, manufacturer, model, version, author, connectorType, jsonConfigString }`
  или `throw new Error(<русский текст>)`. Проверки по порядку:
  1. текст — корректный JSON, иначе «Файл не является корректным JSON»;
  2. `format === 'wintime-control-template'`, иначе «Файл не является шаблоном Wintime Control»;
  3. `formatVersion` указан, иначе «В файле не указана версия формата»; целое ≥ 1 и ≤ 1,
     иначе «Версия формата N не поддерживается»;
  4. `template` — объект, `template.name` — непустая строка, иначе «В файле нет наименования шаблона»;
  5. `jsonConfig` — объект (если есть), иначе «JSON-конфигурация в файле должна быть объектом».

  Отсутствующие необязательные поля → `""`, `version` → `"1.0"`, `connectorType` → `""`
  (форма), `jsonConfigString` — `JSON.stringify(jsonConfig ?? {}, null, 2)`.
- `formToRequest(form)` → тело запроса create/update
  (`connectorType: form.connectorType || null`, `jsonConfig` — распарсенный объект;
  при неверном JSON — `throw new Error('Неверный формат JSON-конфигурации')`).
  Выносится из `saveTemplate`, чтобы путь «импорт → сохранение» был одним тестируемым кодом.

### `src/views/admin/TemplatesView.vue`

- Колонка «Действия»: третья кнопка «Экспорт» → `buildTemplateExport` +
  `templateExportFileName` → `Blob` (`application/json`) → временная ссылка `<a download>`
  → `URL.revokeObjectURL`.
- Шапка: кнопка «Импорт» рядом с «Новый шаблон» + скрытый
  `<input type="file" accept=".json,application/json">`. После чтения файла
  (`file.text()`): `parseTemplateImport` → `editingTemplate = null` →
  `Object.assign(form, …)` → `dialogVisible = true`. Ошибка → `ElMessage.error(err.message)`.
  `input.value = ''` после обработки (повторный выбор того же файла).
- `saveTemplate` использует `formToRequest`; поведение не меняется.

Импорт всегда создаёт новый шаблон. Проверки дубликатов по имени нет (имя не уникально).

## Тесты

### Vitest — `src/utils/__tests__/templateFile.spec.js`

- `buildTemplateExport`: структура, `jsonConfig` как объект, нормализация
  (`null`→`""`, пустой `connectorType`→`null`), ошибка на некорректном `jsonConfig`.
- `templateExportFileName`: замена недопустимых символов и пробелов.
- `parseTemplateImport`: каждая из 5 ошибок; отсутствующие необязательные поля → умолчания.
- `formToRequest`: пустой `connectorType` → `null`, ошибка на битом JSON.
- **Круговой тест** (на наборах: полный шаблон; шаблон с пустыми необязательными полями
  и без `connectorType`; `jsonConfig` с кириллицей и дробными числами `0.5`, `1.5`):
  `buildTemplateExport(t)` → `JSON.stringify` → `parseTemplateImport` → `formToRequest`
  → имитация сохранения с нормализацией как в `TemplatesController.Create`
  (`?? ""` для строк, `jsonConfig` → `JSON.stringify`) → `buildTemplateExport` →
  `toEqual` с первым экспортом без поля `exportedAt`.
  Сравнение — по данным, не побайтно: `exportedAt` всегда различается, а сервер
  переформатирует `jsonConfig` (отступы, `\uXXXX` для кириллицы).

### xUnit — `Wintime.Control.Tests.Integration/Templates/TemplateJsonConfigRoundTripTests.cs`

Закрывает часть круга, невидимую фронтовому тесту: реальную сериализацию
`System.Text.Json` и хранение в колонке `text`.

- `POST /api/templates` с `jsonConfig`, содержащим кириллицу и числа `0.5`/`1.5`,
  затем `GET` → распарсенный `jsonConfig` семантически равен исходному
  (`JsonNode.DeepEquals`).
- То же для `PUT` (Update сохраняет через `.ToString()`, Create — через
  `JsonSerializer.Serialize`: разные пути, проверяем оба).

## Вне скоупа

- Импорт с перезаписью существующего шаблона.
- Пакетный экспорт/импорт нескольких шаблонов.
- Выравнивание Create/Update в контроллере (если интеграционный тест покажет
  семантическое расхождение — отдельный баг-фикс).
