# PZP-09 — Справочник изделий (ProductType) + связь `Mold → ProductType`

- **Дата:** 2026-07-25
- **Задача:** PZP-09 (спринт ПЗП «фундамент заказов», срок 30 июля)
- **Статус:** дизайн утверждён, готов к плану реализации

## Проблема

В системе нет явной сущности «тип изделия / номенклатурная позиция». Сейчас
`Mold` (пресс-форма) неявно = «что производим». Это блокирует:

- **PZP-05 (модуль заказов)** — заказ = одна номенклатурная позиция; прогресс заказа
  скатывается по типу изделия через задание → ПФ → тип изделия.
- **BL-27 (партии / цифровой паспорт)** — паспорт привязан к типу изделия.

PZP-09 вводит каталог изделий **один раз** как общую основу для обеих задач. Объём —
минимальный: сущность + связь с ПФ, без атрибутов паспорта/единиц (добавятся в BL-27).

## Решения (зафиксированы при брейнсторминге 2026-07-25)

1. **Набор полей `ProductType`** — минимальный: `Article` (уникальный, человекочитаемый,
   для связи с внешними системами) + `Name` + `IsActive`. Суррогатный `Id` (Guid) из `BaseEntity`
   — ключ СУБД и цель FK.
2. **`ProductType → Mold = 1:N`** — у одной номенклатуры может быть несколько ПФ.
3. **`Mold.ProductTypeId` — nullable** (старые ПФ созданы без типа), но **форма создания/
   редактирования ПФ требует выбрать тип** → обязательна для новых ПФ.
4. **ADR — пишем** короткий (ADR-0007): каталог изделий как общая номенклатурная основа —
   решение задаёт паттерн для PZP-05/BL-27.
5. **Архивирование `ProductType` с привязанными ПФ — разрешаем** (`IsActive=false`, скрыт из
   селектов новых ПФ; существующие ПФ сохраняют ссылку). Единообразно с архивным `IsActive`
   (CLAUDE.md). Без блокировки.

## Доменная модель

Новая сущность:

```csharp
namespace Wintime.Control.Core.Entities;

public class ProductType : BaseEntity   // Id (Guid) — суррогатный ключ / FK-цель
{
    public string Article { get; set; } = string.Empty; // уникальный, человекочитаемый (CRM/1С)
    public string Name { get; set; } = string.Empty;    // наименование изделия
    public bool IsActive { get; set; } = true;          // архивный флаг (мягкое удаление)
    public ICollection<Mold> Molds { get; set; } = new List<Mold>();
}
```

Правка `Mold`:

```csharp
public Guid? ProductTypeId { get; set; }         // nullable — старые ПФ без типа
public ProductType? ProductType { get; set; }    // навигация
```

`Article` — уникальный, как `Mold.FormId`: unique-индекс, `.Trim()`, exact match,
конфликт → 409.

## EF Core / БД

`ControlDbContext`:

- `DbSet<ProductType> ProductTypes`.
- Конфиг в `OnModelCreating`:
  ```csharp
  builder.Entity<ProductType>(e =>
  {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.Article).IsUnique();
  });
  builder.Entity<ProductType>().ToTable("ProductTypes");
  // в блоке Mold: связь 1:N
  builder.Entity<Mold>()
      .HasOne(m => m.ProductType)
      .WithMany(pt => pt.Molds)
      .HasForeignKey(m => m.ProductTypeId)
      .OnDelete(DeleteBehavior.SetNull);  // тип физически не удаляют (IsActive), правило единообразно
  ```
- Одна миграция `AddProductType`: таблица `ProductTypes` + колонка `Molds.ProductTypeId`
  (nullable) + unique-индекс на `Article` + индекс/FK на `Molds.ProductTypeId`.

## Backend — API

`ProductTypesController` по образцу `MoldsController`. Роли: чтение —
`Admin,Manager,Adjuster`; запись — `Admin,Manager`.

| Метод | Маршрут | Назначение |
| --- | --- | --- |
| GET | `/api/producttypes?isActive=&search=` | список (фильтры: `isActive`, поиск по `Article`/`Name`) |
| GET | `/api/producttypes/{id:guid}` | по id |
| POST | `/api/producttypes` | создать (валидация уникальности `Article`, 409 при конфликте) |
| PUT | `/api/producttypes/{id:guid}` | обновить (PATCH-семантика, `IsActive` для архивирования) |

DTO (папка `DTOs/ProductType/`): `ProductTypeDto`, `CreateProductTypeRequestDto`,
`UpdateProductTypeRequestDto`. Полей мало (`Id`/`Article`/`Name`/`IsActive`),
маппинг вручную (как в `MoldsController`, без AutoMapper).

**Правки `MoldsController` / DTO ПФ:**

- `MoldDto` дополняется: `ProductTypeId` (Guid?), `ProductTypeArticle` (string?),
  `ProductTypeName` (string?) — для отображения без второго запроса. Список ПФ подмешивает
  их через `Include(m => m.ProductType)` (или join-словарь, как сделан `cycleMap`).
- `CreateMoldRequestDto` получает `ProductTypeId` (Guid?). **`CreateMold` требует непустой
  `ProductTypeId`** → `BadRequest`, если не задан («обязательна для новых ПФ»). Опционально
  валидировать существование/активность типа.
- `UpdateMoldRequestDto` получает `ProductTypeId` (Guid?). `UpdateMold` **не** блокирует
  старые ПФ без типа (поле опционально в запросе; задаётся → перепривязывает).

## Frontend

- `src/api/productTypes.js` — по образцу `src/api/molds.js` (list/get/create/update).
- `src/views/dictionary/ProductTypeDictionary.vue` — новый справочник по паттерну
  `MoldDictionary.vue`: таблица (Артикул / Наименование / Статус), фильтры (поиск,
  активность), create/edit-модалка, архивирование через `IsActive`. Пункт в роутере
  (`src/router/index.js`) и в навигационном меню.
- Правки `MoldDictionary.vue`:
  - колонка «Тип изделия» в таблице (показывает `productTypeName` / `productTypeArticle`);
  - **обязательное** поле-селект `ProductType` в форме создания/редактирования ПФ:
    `el-select` filterable, грузит только активные типы (`isActive=true`), с правилом
    валидации (нельзя сохранить ПФ без выбранного типа).

## Тесты

- **xUnit (интеграционные):**
  - `ProductTypesController`: CRUD; конфликт уникального `Article` (409); архивирование
    (`IsActive=false`) типа с привязанной ПФ проходит, ПФ сохраняет ссылку; фильтры
    `isActive`/`search`.
  - `MoldsController`: `CreateMold` без `ProductTypeId` → 400; с валидным → ПФ привязана,
    `MoldDto` содержит `productTypeName`; `UpdateMold` старой ПФ без типа не падает.
- **Vitest:** smoke `ProductTypeDictionary.vue` (рендер, создание); обязательность селекта
  типа в форме ПФ (`MoldDictionary.vue`) — сохранение без типа блокируется.

## Границы объёма (YAGNI — НЕ входит в PZP-09)

- Атрибуты паспорта, единицы измерения, описание, профиль по типу процесса → BL-27.
- Сущность «Заказ», `ShiftTask.OrderId`, прогресс заказа → PZP-05.
- Валидация «ПФ задания ≠ тип заказа» → PZP-05.
- Обобщение каталога за пределы ТПА (generic-профиль) — принцип заложен ADR, реализация позже.

## Связи

- **PZP-05** (заказы) — прямой потребитель каталога: `Order` ссылается на `ProductType`,
  прогресс скатывается через задание → ПФ → тип.
- **BL-27** (партии/паспорт) — паспорт привязан к типу изделия; расширяет `ProductType`
  атрибутами.
- **ADR-0007** — фиксирует каталог как общую номенклатурную основу.
