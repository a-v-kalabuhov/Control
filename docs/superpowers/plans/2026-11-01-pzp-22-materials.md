# PZP-22 Справочники материалов — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Справочник материалов (пластики и красители): сущность `Material`, API `/api/materials` с подсказками, два экрана справочника на одном компоненте и общий компонент подсказок `SuggestInput`.

**Architecture:** Одна таблица `Materials` с enum-типом `MaterialType` (int). Нормализация и валидация — чистая политика `Core/Policies/MaterialRules`; контроллер вызывает её, проверяет уникальность в БД и отдаёт 409. Регистронезависимую уникальность `(Type, Manufacturer, Name)` страхует unique-индекс на выражении (сырой SQL в миграции). Фронт: `MaterialDictionary.vue` берёт тип из `meta.materialType` маршрута.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions + Testcontainers; Vue 3 + Element Plus, Vitest + @vue/test-utils.

**Спека:** `docs/superpowers/specs/2026-11-01-pzp-22-materials-design.md`

## Global Constraints

- Ветка: `feature/pzp-22-materials` (уже создана от master, в ней коммиты спеки). Не пушить в master.
- Тип материала задаётся при создании и **не меняется**; PUT поле `type` не принимает.
- Обязательны: артикул и наименование; у `Colorant` ещё `ColorName`. Остальное необязательно.
- Артикул: Trim + верхний регистр (`ProductArticle.Normalize`). Остальные строки: Trim, пусто → `null`. `ColorCode` регистр не меняет.
- Длины: Article ≤ 50, Name ≤ 200, Manufacturer ≤ 200, ColorName ≤ 100, ColorCode ≤ 50, Comment ≤ 1000.
- Уникальность внутри типа без учёта регистра: `(Type, Article)` и `(Type, Manufacturer, Name)`; пустой производитель — одно значение. Между типами совпадения допустимы.
- Архив — `IsActive` через тот же `PUT` (`{ isActive: false }`), как в остальных справочниках.
- Физическое удаление — только без ссылок; в PZP-22 ссылок нет (точка расширения для BL-36).
- Подсказки — distinct по обоим типам, включая архивные, максимум 20.
- Роли: чтение `Admin, Manager, Adjuster, Observer`; запись, удаление и подсказки — `Admin, Manager`. Экраны — `Admin, Manager`.
- Тексты UI и сообщений — на русском. Комментарии в коде — на русском, в стиле окружающего кода.
- Миграции: `dotnet ef migrations add <Name> --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API` (оба флага обязательны).
- Каждый коммит заканчивается строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Не трогать посторонний изменённый файл `docs/superpowers/specs/2026-08-10-borche-emulator-design.md` — не добавлять в коммиты (`git add` только конкретных путей, никогда `git add -A`/`-a`).

## Файловая структура

| Файл | Ответственность |
|---|---|
| `Wintime.Control.Core/Enums/MaterialType.cs` | enum типов материала |
| `Wintime.Control.Core/Entities/Material.cs` | сущность |
| `Wintime.Control.Core/Policies/MaterialRules.cs` | нормализация + валидация (чистые функции) |
| `Wintime.Control.Core/DTOs/Material/*.cs` | DTO запросов/ответов |
| `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` | DbSet + конфигурация |
| `Wintime.Control.Infrastructure/Migrations/*_AddMaterials.cs` | таблица + индексы |
| `Wintime.Control.API/Controllers/MaterialsController.cs` | CRUD + подсказки |
| `Wintime.Control.Tests.Unit/Policies/MaterialRulesTests.cs` | юнит-тесты политики |
| `Wintime.Control.Tests.Integration/Materials/MaterialEntityTests.cs` | индексы БД |
| `Wintime.Control.Tests.Integration/Materials/MaterialsControllerTests.cs` | API |
| `Wintime-Control-Frontend/src/api/materials.js` | клиент API |
| `Wintime-Control-Frontend/src/components/common/SuggestInput.vue` | общий ввод с подсказками |
| `Wintime-Control-Frontend/src/views/dictionary/MaterialDictionary.vue` | экран справочника |
| `Wintime-Control-Frontend/src/router/index.js`, `src/layouts/DefaultLayout.vue` | маршруты и меню |
| `docs/domain/material.md`, `docs/domain/README.md`, `CLAUDE.md`, `docs/user-guide/manager.md`, `VERSION` | документация, версия |

---

### Task 1: Ядро — enum, сущность, политика `MaterialRules`

**Files:**
- Create: `Wintime.Control.Core/Enums/MaterialType.cs`
- Create: `Wintime.Control.Core/Entities/Material.cs`
- Create: `Wintime.Control.Core/Policies/MaterialRules.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/MaterialRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum MaterialType { Plastic = 0, Colorant = 1 }` (namespace `Wintime.Control.Core.Enums`)
  - `class Material : BaseEntity { MaterialType Type; string Article; string Name; string? Manufacturer; string? ColorName; string? ColorCode; string? Comment; bool IsActive = true; }`
  - `record MaterialFields(string Article, string Name, string? Manufacturer, string? ColorName, string? ColorCode, string? Comment)`
  - `static MaterialFields MaterialRules.Normalize(string? article, string? name, string? manufacturer, string? colorName, string? colorCode, string? comment)`
  - `static string? MaterialRules.Validate(MaterialType type, MaterialFields f)` — `null` = ок, иначе текст ошибки
  - `static string? MaterialRules.Clean(string? value)` — Trim, пусто → `null`
  - `static string MaterialRules.TypeLabel(MaterialType type)` — «пластик» / «краситель» (для сообщений 409)
  - константы `MaterialRules.ArticleMaxLength = 50`, `NameMaxLength = 200`, `ManufacturerMaxLength = 200`, `ColorNameMaxLength = 100`, `ColorCodeMaxLength = 50`, `CommentMaxLength = 1000`

- [ ] **Step 1: Написать падающие тесты**

`Wintime.Control.Tests.Unit/Policies/MaterialRulesTests.cs`:

```csharp
using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class MaterialRulesTests
{
    private static MaterialFields Fields(string article = "PP-01", string name = "Полипропилен",
        string? manufacturer = null, string? colorName = null, string? colorCode = null, string? comment = null) =>
        MaterialRules.Normalize(article, name, manufacturer, colorName, colorCode, comment);

    [Fact]
    public void Normalize_UppercasesArticleAndTrimsAll()
    {
        var f = MaterialRules.Normalize("  pp-01 ", "  Полипропилен ", " Сибур ", " Чёрный ", " ral 9005 ", " прим ");

        f.Article.Should().Be("PP-01");
        f.Name.Should().Be("Полипропилен");
        f.Manufacturer.Should().Be("Сибур");
        f.ColorName.Should().Be("Чёрный");
        f.ColorCode.Should().Be("ral 9005");   // регистр кода цвета не трогаем
        f.Comment.Should().Be("прим");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyOptionalBecomesNull(string? value)
    {
        var f = MaterialRules.Normalize("A", "B", value, value, value, value);

        f.Manufacturer.Should().BeNull();
        f.ColorName.Should().BeNull();
        f.ColorCode.Should().BeNull();
        f.Comment.Should().BeNull();
    }

    [Fact]
    public void Normalize_NullRequiredBecomesEmpty()
    {
        var f = MaterialRules.Normalize(null, null, null, null, null, null);
        f.Article.Should().BeEmpty();
        f.Name.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Plastic_WithoutColor_IsValid() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields()).Should().BeNull();

    [Fact]
    public void Validate_Colorant_WithoutColor_Fails() =>
        MaterialRules.Validate(MaterialType.Colorant, Fields()).Should().Be("Для красителя укажите цвет.");

    [Fact]
    public void Validate_Colorant_WithColor_IsValid() =>
        MaterialRules.Validate(MaterialType.Colorant, Fields(colorName: "Чёрный")).Should().BeNull();

    [Fact]
    public void Validate_EmptyArticle_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(article: "  ")).Should().Be("Артикул обязателен.");

    [Fact]
    public void Validate_EmptyName_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(name: "")).Should().Be("Наименование обязательно.");

    [Fact]
    public void Validate_TooLongArticle_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(article: new string('A', MaterialRules.ArticleMaxLength + 1)))
            .Should().Be($"Артикул длиннее {MaterialRules.ArticleMaxLength} символов.");

    [Fact]
    public void Validate_TooLongComment_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(comment: new string('x', MaterialRules.CommentMaxLength + 1)))
            .Should().Be($"Комментарий длиннее {MaterialRules.CommentMaxLength} символов.");

    [Theory]
    [InlineData(MaterialType.Plastic, "пластик")]
    [InlineData(MaterialType.Colorant, "краситель")]
    public void TypeLabel_IsRussian(MaterialType type, string expected) =>
        MaterialRules.TypeLabel(type).Should().Be(expected);
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~MaterialRulesTests"`
Expected: ошибка компиляции — `MaterialRules`/`MaterialType` не найдены.

- [ ] **Step 3: Реализация**

`Wintime.Control.Core/Enums/MaterialType.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

/// <summary>
/// Тип материала (PZP-22). Хранится в БД как int (колонка Materials.Type) — значения фиксированы.
/// Новый тип (добавки, регранулят) добавляется значением enum. Тип материала после создания не меняется.
/// </summary>
public enum MaterialType
{
    Plastic = 0,   // Пластик
    Colorant = 1   // Краситель
}
```

`Wintime.Control.Core/Entities/Material.cs`:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Материал — пластик или краситель (PZP-22). Ингредиент рецепта изделия (BL-36).
/// Уникальность внутри типа без учёта регистра: (тип, артикул) и (тип, производитель, наименование).
/// </summary>
public class Material : BaseEntity
{
    public MaterialType Type { get; set; }               // задаётся при создании, не меняется
    public string Article { get; set; } = string.Empty;  // заглавными (ProductArticle.Normalize)
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }               // обязателен у красителя
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;           // архивный флаг (мягкое удаление)
}
```

`Wintime.Control.Core/Policies/MaterialRules.cs`:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>Нормализованные поля материала (PZP-22).</summary>
public sealed record MaterialFields(
    string Article, string Name, string? Manufacturer, string? ColorName, string? ColorCode, string? Comment);

/// <summary>
/// Нормализация и проверка полей материала (PZP-22). Уникальность проверяет контроллер по БД.
/// </summary>
public static class MaterialRules
{
    public const int ArticleMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int ManufacturerMaxLength = 200;
    public const int ColorNameMaxLength = 100;
    public const int ColorCodeMaxLength = 50;
    public const int CommentMaxLength = 1000;

    /// <summary>Trim; пустая строка → <c>null</c>.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static MaterialFields Normalize(string? article, string? name, string? manufacturer,
        string? colorName, string? colorCode, string? comment) =>
        new(ProductArticle.Normalize(article),
            (name ?? string.Empty).Trim(),
            Clean(manufacturer),
            Clean(colorName),
            Clean(colorCode),
            Clean(comment));

    public static string? Validate(MaterialType type, MaterialFields f)
    {
        if (f.Article.Length == 0) return "Артикул обязателен.";
        if (f.Name.Length == 0) return "Наименование обязательно.";
        if (type == MaterialType.Colorant && f.ColorName == null) return "Для красителя укажите цвет.";

        return TooLong(f.Article, ArticleMaxLength, "Артикул")
            ?? TooLong(f.Name, NameMaxLength, "Наименование")
            ?? TooLong(f.Manufacturer, ManufacturerMaxLength, "Производитель")
            ?? TooLong(f.ColorName, ColorNameMaxLength, "Цвет")
            ?? TooLong(f.ColorCode, ColorCodeMaxLength, "Код цвета")
            ?? TooLong(f.Comment, CommentMaxLength, "Комментарий");
    }

    public static string TypeLabel(MaterialType type) => type switch
    {
        MaterialType.Plastic => "пластик",
        MaterialType.Colorant => "краситель",
        _ => type.ToString()
    };

    private static string? TooLong(string? value, int max, string label) =>
        value != null && value.Length > max ? $"{label} длиннее {max} символов." : null;
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~MaterialRulesTests"`
Expected: все PASS.

- [ ] **Step 5: Коммит**

```bash
git add Wintime.Control.Core/Enums/MaterialType.cs Wintime.Control.Core/Entities/Material.cs Wintime.Control.Core/Policies/MaterialRules.cs Wintime.Control.Tests.Unit/Policies/MaterialRulesTests.cs
git commit -m "feat(materials): Material entity, MaterialType and MaterialRules policy (PZP-22)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: БД — DbSet, конфигурация, миграция с индексом на выражении

**Files:**
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` (DbSet после `PlanningDefaults` ~строка 30; конфигурация после блока `Product` ~строка 98)
- Create: `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddMaterials.cs` (+ Designer, + snapshot — генерирует `dotnet ef`)
- Test: `Wintime.Control.Tests.Integration/Materials/MaterialEntityTests.cs`

**Interfaces:**
- Consumes: `Material`, `MaterialType`, константы длин `MaterialRules.*MaxLength` (Task 1).
- Produces: `ControlDbContext.Materials` (`DbSet<Material>`); таблица `Materials`; индексы `IX_Materials_Type_Article` (unique), `IX_Materials_Type_IsActive`, `IX_Materials_Type_Manufacturer_Name` (unique, на выражении).

- [ ] **Step 1: Написать падающие тесты**

`Wintime.Control.Tests.Integration/Materials/MaterialEntityTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Materials;

[Collection("Integration")]
public class MaterialEntityTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MaterialEntityTests(IntegrationTestFactory factory) => _factory = factory;

    private ControlDbContext NewDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    private static string Uniq(string prefix) => $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();

    [Fact]
    public async Task Material_Persists()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var m = new Material { Type = MaterialType.Colorant, Article = Uniq("C"), Name = "Краситель", ColorName = "Чёрный" };
        db.Materials.Add(m);
        await db.SaveChangesAsync();

        var loaded = await db.Materials.AsNoTracking().FirstAsync(x => x.Id == m.Id);
        loaded.Type.Should().Be(MaterialType.Colorant);
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SameTypeAndArticle_ViolatesUniqueIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var article = Uniq("P");
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = article, Name = Uniq("A") });
        await db.SaveChangesAsync();

        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = article, Name = Uniq("B") });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SameArticle_DifferentType_IsAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var article = Uniq("X");
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = article, Name = Uniq("A") });
        db.Materials.Add(new Material { Type = MaterialType.Colorant, Article = article, Name = Uniq("B"), ColorName = "Белый" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ManufacturerAndName_CaseInsensitive_ViolatesExpressionIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var name = $"Полипропилен {Guid.NewGuid():N}";
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name, Manufacturer = "Сибур" });
        await db.SaveChangesAsync();

        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name.ToUpperInvariant(), Manufacturer = "СИБУР" });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task NullManufacturer_IsOneValue_ForUniqueness()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var name = $"Без производителя {Guid.NewGuid():N}";
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name });
        await db.SaveChangesAsync();

        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MaterialEntityTests"`
Expected: ошибка компиляции — у `ControlDbContext` нет `Materials`. (Нужен запущенный Docker для Testcontainers.)

- [ ] **Step 3: DbSet и конфигурация**

В `ControlDbContext.cs` после `public DbSet<PlanningDefaults> PlanningDefaults { get; set; }`:

```csharp
    public DbSet<Material> Materials { get; set; }
```

В `OnModelCreating` после блока `builder.Entity<Product>(...)`:

```csharp
        // Конфигурация Material (PZP-22): уникальность внутри типа.
        // Регистронезависимый индекс (Type, lower(coalesce(Manufacturer,'')), lower(Name)) — на выражении,
        // EF его не моделирует, создаётся сырым SQL в миграции AddMaterials.
        builder.Entity<Material>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Article).HasMaxLength(MaterialRules.ArticleMaxLength).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(MaterialRules.NameMaxLength).IsRequired();
            entity.Property(e => e.Manufacturer).HasMaxLength(MaterialRules.ManufacturerMaxLength);
            entity.Property(e => e.ColorName).HasMaxLength(MaterialRules.ColorNameMaxLength);
            entity.Property(e => e.ColorCode).HasMaxLength(MaterialRules.ColorCodeMaxLength);
            entity.Property(e => e.Comment).HasMaxLength(MaterialRules.CommentMaxLength);
            entity.HasIndex(e => new { e.Type, e.Article }).IsUnique();
            entity.HasIndex(e => new { e.Type, e.IsActive });
            entity.ToTable("Materials");
        });
```

Добавить `using Wintime.Control.Core.Policies;` в начало файла, если его нет.

- [ ] **Step 4: Сгенерировать миграцию**

Run (из корня репозитория):
`dotnet ef migrations add AddMaterials --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API`
Expected: `Done.` и новые файлы `Migrations/<timestamp>_AddMaterials.cs`, `.Designer.cs`, обновлённый `ControlDbContextModelSnapshot.cs`.

- [ ] **Step 5: Дописать индекс на выражении в миграцию**

В сгенерированном `<timestamp>_AddMaterials.cs`: в конец `Up` (после `CreateIndex`) добавить:

```csharp
            // Регистронезависимая уникальность (тип, производитель, наименование); пустой производитель — одно значение.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Materials_Type_Manufacturer_Name\" ON \"Materials\" " +
                "(\"Type\", lower(coalesce(\"Manufacturer\", '')), lower(\"Name\"));");
```

В начало `Down` (до `DropTable`) добавить:

```csharp
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Materials_Type_Manufacturer_Name\";");
```

Проверить, что сгенерированный `Up` создаёт таблицу `Materials` и только её (никаких посторонних изменений модели). Если есть посторонние операции — остановиться и сообщить.

- [ ] **Step 6: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MaterialEntityTests"`
Expected: 5 PASS (фабрика применяет миграции при старте).

- [ ] **Step 7: Коммит**

```bash
git add Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations Wintime.Control.Tests.Integration/Materials/MaterialEntityTests.cs
git commit -m "feat(materials): Materials table with case-insensitive unique indexes (PZP-22)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: API — DTO и `MaterialsController`

**Files:**
- Create: `Wintime.Control.Core/DTOs/Material/MaterialDto.cs`
- Create: `Wintime.Control.Core/DTOs/Material/CreateMaterialRequestDto.cs`
- Create: `Wintime.Control.Core/DTOs/Material/UpdateMaterialRequestDto.cs`
- Create: `Wintime.Control.Core/DTOs/Material/MaterialColorSuggestionDto.cs`
- Create: `Wintime.Control.API/Controllers/MaterialsController.cs`
- Test: `Wintime.Control.Tests.Integration/Materials/MaterialsControllerTests.cs`

**Interfaces:**
- Consumes: `Material`, `MaterialType`, `MaterialRules.Normalize/Validate/TypeLabel` (Task 1); `ControlDbContext.Materials` (Task 2).
- Produces (JSON, camelCase, enum — строкой `"Plastic"`/`"Colorant"` через глобальный `JsonStringEnumConverter`):
  - `GET /api/materials?type=&isActive=&search=` → `MaterialDto[]`
  - `GET /api/materials/{id}` → `MaterialDto`
  - `POST /api/materials` (`CreateMaterialRequestDto`) → 201 `MaterialDto`; 400 текст; 409 текст
  - `PUT /api/materials/{id}` (`UpdateMaterialRequestDto`) → 204; 400; 404; 409
  - `DELETE /api/materials/{id}` → 204; 404; 409
  - `GET /api/materials/suggestions/manufacturers?search=` → `string[]`
  - `GET /api/materials/suggestions/colors?search=` → `MaterialColorSuggestionDto[]` (`{ name, code }`)
  - `MaterialDto { id, type, article, name, manufacturer, colorName, colorCode, comment, isActive, isUsed }`

- [ ] **Step 1: DTO**

`Wintime.Control.Core/DTOs/Material/MaterialDto.cs`:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Material;

public class MaterialDto
{
    public Guid Id { get; set; }
    public MaterialType Type { get; set; }
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; }
    public bool IsUsed { get; set; }   // есть ссылки (рецепты, BL-36) — удалить нельзя, только в архив
}
```

`CreateMaterialRequestDto.cs`:

```csharp
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Material;

public class CreateMaterialRequestDto
{
    public MaterialType Type { get; set; }
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
}
```

`UpdateMaterialRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Material;

/// <summary>
/// Частичная правка: <c>null</c> — поле не меняется, пустая строка у необязательного поля — очистить.
/// Тип материала не меняется (PZP-22), поэтому его здесь нет.
/// </summary>
public class UpdateMaterialRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
    public bool? IsActive { get; set; }
}
```

`MaterialColorSuggestionDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Material;

public class MaterialColorSuggestionDto
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
}
```

- [ ] **Step 2: Написать падающие тесты**

`Wintime.Control.Tests.Integration/Materials/MaterialsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Material;
using Wintime.Control.Core.Enums;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Materials;

[Collection("Integration")]
public class MaterialsControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MaterialsControllerTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ManagerAsync() => ClientAsync("test_manager", "Manager123!");
    private Task<HttpClient> AdjusterAsync() => ClientAsync("test_adjuster", "Adjuster123!");

    private static string Uniq(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task<MaterialDto> CreateAsync(HttpClient client, CreateMaterialRequestDto req)
    {
        var resp = await client.PostAsJsonAsync("/api/materials", req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<MaterialDto>())!;
    }

    private static CreateMaterialRequestDto Plastic(string? article = null, string? name = null, string? manufacturer = null) =>
        new() { Type = MaterialType.Plastic, Article = article ?? Uniq("pp"), Name = name ?? Uniq("Полипропилен"), Manufacturer = manufacturer };

    [Fact]
    public async Task Create_NormalizesAndReturnsCreated()
    {
        var client = await ManagerAsync();
        var article = Uniq("pp");

        var dto = await CreateAsync(client, Plastic(article: $"  {article} ", manufacturer: "  Сибур "));

        dto.Article.Should().Be(article.ToUpperInvariant());
        dto.Manufacturer.Should().Be("Сибур");
        dto.Type.Should().Be(MaterialType.Plastic);
        dto.IsActive.Should().BeTrue();
        dto.IsUsed.Should().BeFalse();

        var get = await client.GetFromJsonAsync<MaterialDto>($"/api/materials/{dto.Id}");
        get!.Article.Should().Be(article.ToUpperInvariant());
    }

    [Fact]
    public async Task Create_ColorantWithoutColor_Returns400()
    {
        var client = await ManagerAsync();
        var resp = await client.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequestDto { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель") });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("цвет");
    }

    [Fact]
    public async Task Create_DuplicateArticle_DifferentCase_Returns409()
    {
        var client = await ManagerAsync();
        var article = Uniq("pp");
        await CreateAsync(client, Plastic(article: article));

        var resp = await client.PostAsJsonAsync("/api/materials", Plastic(article: $" {article.ToLowerInvariant()} "));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("пластик");
    }

    [Fact]
    public async Task Create_DuplicateManufacturerAndName_CaseInsensitive_Returns409()
    {
        var client = await ManagerAsync();
        var name = Uniq("Полипропилен");
        await CreateAsync(client, Plastic(name: name, manufacturer: "Сибур"));

        var resp = await client.PostAsJsonAsync("/api/materials", Plastic(name: name.ToUpperInvariant(), manufacturer: "сибур"));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_DuplicateName_BothWithoutManufacturer_Returns409()
    {
        var client = await ManagerAsync();
        var name = Uniq("Без производителя");
        await CreateAsync(client, Plastic(name: name));

        var resp = await client.PostAsJsonAsync("/api/materials", Plastic(name: name));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameArticleAsOtherType_IsAllowed()
    {
        var client = await ManagerAsync();
        var article = Uniq("x");
        await CreateAsync(client, Plastic(article: article));
        await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = article, Name = Uniq("Краситель"), ColorName = "Чёрный" });
    }

    [Fact]
    public async Task Update_ChangesFields_ClearsOptional_AndExcludesSelfFromUniqueness()
    {
        var client = await ManagerAsync();
        var created = await CreateAsync(client, Plastic(manufacturer: "Сибур"));

        // Тот же артикул (сам себя) + очистка производителя пустой строкой
        var resp = await client.PutAsJsonAsync($"/api/materials/{created.Id}",
            new UpdateMaterialRequestDto { Article = created.Article.ToLowerInvariant(), Manufacturer = "", Comment = "прим" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetFromJsonAsync<MaterialDto>($"/api/materials/{created.Id}");
        get!.Manufacturer.Should().BeNull();
        get.Comment.Should().Be("прим");
        get.Article.Should().Be(created.Article);
    }

    [Fact]
    public async Task Update_ToOtherMaterialsArticle_Returns409()
    {
        var client = await ManagerAsync();
        var a = await CreateAsync(client, Plastic());
        var b = await CreateAsync(client, Plastic());

        var resp = await client.PutAsJsonAsync($"/api/materials/{b.Id}", new UpdateMaterialRequestDto { Article = a.Article });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ClearColorOfColorant_Returns400()
    {
        var client = await ManagerAsync();
        var c = await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = "Чёрный" });

        var resp = await client.PutAsJsonAsync($"/api/materials/{c.Id}", new UpdateMaterialRequestDto { ColorName = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_IgnoresType_TypeStaysTheSame()
    {
        var client = await ManagerAsync();
        var p = await CreateAsync(client, Plastic());

        // Лишнее поле type в теле не должно менять тип (в DTO его нет).
        var resp = await client.PutAsJsonAsync($"/api/materials/{p.Id}", new { type = "Colorant", name = p.Name });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetFromJsonAsync<MaterialDto>($"/api/materials/{p.Id}");
        get!.Type.Should().Be(MaterialType.Plastic);
    }

    [Fact]
    public async Task Archive_AndRestore_ViaPut_AndFilterByActivity()
    {
        var client = await ManagerAsync();
        var p = await CreateAsync(client, Plastic());

        (await client.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { IsActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var archived = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Plastic&isActive=false&search={p.Article}");
        archived!.Should().ContainSingle(x => x.Id == p.Id);

        (await client.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { IsActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var active = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Plastic&isActive=true&search={p.Article}");
        active!.Should().ContainSingle(x => x.Id == p.Id);
    }

    [Fact]
    public async Task GetList_FiltersByType_AndSearchesManufacturerAndColor()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N");
        var plastic = await CreateAsync(client, Plastic(manufacturer: $"Завод {marker}"));
        var colorant = await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = $"Цвет {marker}" });

        var plastics = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Plastic&search={marker}");
        plastics!.Select(x => x.Id).Should().BeEquivalentTo(new[] { plastic.Id });

        var colorants = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Colorant&search={marker}");
        colorants!.Select(x => x.Id).Should().BeEquivalentTo(new[] { colorant.Id });
    }

    [Fact]
    public async Task Delete_Unused_Returns204_ThenNotFound()
    {
        var client = await ManagerAsync();
        var p = await CreateAsync(client, Plastic());

        (await client.DeleteAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Suggestions_Manufacturers_DistinctAcrossTypes_IncludingArchived()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var manufacturer = $"Производитель {marker}";
        var p = await CreateAsync(client, Plastic(manufacturer: manufacturer));
        await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = "Белый", Manufacturer = manufacturer });
        await client.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { IsActive = false });

        var list = await client.GetFromJsonAsync<List<string>>($"/api/materials/suggestions/manufacturers?search={marker}");
        list.Should().Equal(manufacturer);
    }

    [Fact]
    public async Task Suggestions_Colors_ReturnsNameCodePairs()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = $"Чёрный {marker}", ColorCode = "RAL 9005" });

        var list = await client.GetFromJsonAsync<List<MaterialColorSuggestionDto>>($"/api/materials/suggestions/colors?search={marker}");
        list.Should().ContainSingle();
        list![0].Name.Should().Be($"Чёрный {marker}");
        list[0].Code.Should().Be("RAL 9005");
    }

    [Fact]
    public async Task Suggestions_AreLimitedTo20()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        for (var i = 0; i < 22; i++)
            await CreateAsync(client, Plastic(manufacturer: $"Лимит {marker} {i:D2}"));

        var list = await client.GetFromJsonAsync<List<string>>($"/api/materials/suggestions/manufacturers?search={marker}");
        list.Should().HaveCount(20);
    }

    [Fact]
    public async Task Adjuster_CanRead_ButCannotWrite()
    {
        var manager = await ManagerAsync();
        var p = await CreateAsync(manager, Plastic());
        var adjuster = await AdjusterAsync();

        (await adjuster.GetAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await adjuster.PostAsJsonAsync("/api/materials", Plastic())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await adjuster.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { Name = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await adjuster.DeleteAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 3: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MaterialsControllerTests"`
Expected: FAIL — 404 на `/api/materials` (контроллера нет).

- [ ] **Step 4: Реализация контроллера**

`Wintime.Control.API/Controllers/MaterialsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Material;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

/// <summary>Справочник материалов — пластики и красители (PZP-22).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private const int SuggestionLimit = 20;

    private readonly ControlDbContext _context;

    public MaterialsController(ControlDbContext context) => _context = context;

    private static MaterialDto ToDto(Material m, ISet<Guid> usedIds) => new()
    {
        Id = m.Id, Type = m.Type, Article = m.Article, Name = m.Name, Manufacturer = m.Manufacturer,
        ColorName = m.ColorName, ColorCode = m.ColorCode, Comment = m.Comment,
        IsActive = m.IsActive, IsUsed = usedIds.Contains(m.Id)
    };

    // Материалы из набора, на которые есть ссылки. Ссылающихся таблиц пока нет —
    // BL-36 добавит рецепты изделий и копии рецептов в заданиях (включая архивные).
    private Task<HashSet<Guid>> UsedMaterialIdsAsync(IReadOnlyCollection<Guid> materialIds) =>
        Task.FromResult(new HashSet<Guid>());

    // Уникальность внутри типа без учёта регистра (артикул уже в верхнем регистре).
    private async Task<string?> UniquenessConflictAsync(MaterialType type, MaterialFields f, Guid? exceptId)
    {
        var label = MaterialRules.TypeLabel(type);
        var byArticle = await _context.Materials
            .FirstOrDefaultAsync(m => m.Type == type && m.Article == f.Article && m.Id != exceptId);
        if (byArticle != null)
            return $"Артикул {f.Article} уже есть у материала «{byArticle.Name}» ({label}).";

        var manufacturerKey = (f.Manufacturer ?? string.Empty).ToLower();
        var nameKey = f.Name.ToLower();
        var byName = await _context.Materials.FirstOrDefaultAsync(m => m.Type == type && m.Id != exceptId
            && (m.Manufacturer ?? string.Empty).ToLower() == manufacturerKey && m.Name.ToLower() == nameKey);
        if (byName == null)
            return null;
        return f.Manufacturer == null
            ? $"Материал «{byName.Name}» без производителя уже есть ({label}, артикул {byName.Article})."
            : $"Материал «{byName.Name}» производителя «{byName.Manufacturer}» уже есть ({label}, артикул {byName.Article}).";
    }

    // Подстраховка от гонки: unique-индекс сработал между проверкой и сохранением.
    private static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    /// <summary>Список материалов (фильтры: тип, активность, поиск по артикулу, наименованию, производителю, цвету).</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<IEnumerable<MaterialDto>>> GetList(
        [FromQuery] MaterialType? type = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        var query = _context.Materials.AsQueryable();
        if (type.HasValue)
            query = query.Where(m => m.Type == type.Value);
        if (isActive.HasValue)
            query = query.Where(m => m.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.Article, pattern) || EF.Functions.ILike(m.Name, pattern)
                || (m.Manufacturer != null && EF.Functions.ILike(m.Manufacturer, pattern))
                || (m.ColorName != null && EF.Functions.ILike(m.ColorName, pattern))
                || (m.ColorCode != null && EF.Functions.ILike(m.ColorCode, pattern)));
        }

        var items = await query.OrderBy(m => m.Article).ToListAsync();
        var usedIds = await UsedMaterialIdsAsync(items.Select(m => m.Id).ToList());
        return Ok(items.Select(m => ToDto(m, usedIds)).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<MaterialDto>> GetById(Guid id)
    {
        var m = await _context.Materials.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null)
            return NotFound();
        return Ok(ToDto(m, await UsedMaterialIdsAsync(new[] { id })));
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<MaterialDto>> Create([FromBody] CreateMaterialRequestDto request)
    {
        if (!Enum.IsDefined(request.Type))
            return BadRequest("Неизвестный тип материала.");
        var f = MaterialRules.Normalize(request.Article, request.Name, request.Manufacturer,
            request.ColorName, request.ColorCode, request.Comment);
        var error = MaterialRules.Validate(request.Type, f);
        if (error != null)
            return BadRequest(error);
        var conflict = await UniquenessConflictAsync(request.Type, f, null);
        if (conflict != null)
            return Conflict(conflict);

        var m = new Material
        {
            Type = request.Type, Article = f.Article, Name = f.Name, Manufacturer = f.Manufacturer,
            ColorName = f.ColorName, ColorCode = f.ColorCode, Comment = f.Comment
        };
        _context.Materials.Add(m);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (IsUniqueViolation(e)) { return Conflict("Такой материал уже есть."); }

        return CreatedAtAction(nameof(GetById), new { id = m.Id }, ToDto(m, new HashSet<Guid>()));
    }

    /// <summary>
    /// Частичная правка и архив/возврат (<c>isActive</c>). <c>null</c> — поле не меняется,
    /// пустая строка у необязательного поля — очистить. Тип не меняется.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMaterialRequestDto request)
    {
        var m = await _context.Materials.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null)
            return NotFound();

        var f = MaterialRules.Normalize(
            request.Article ?? m.Article,
            request.Name ?? m.Name,
            request.Manufacturer ?? m.Manufacturer,
            request.ColorName ?? m.ColorName,
            request.ColorCode ?? m.ColorCode,
            request.Comment ?? m.Comment);
        var error = MaterialRules.Validate(m.Type, f);
        if (error != null)
            return BadRequest(error);
        var conflict = await UniquenessConflictAsync(m.Type, f, m.Id);
        if (conflict != null)
            return Conflict(conflict);

        m.Article = f.Article;
        m.Name = f.Name;
        m.Manufacturer = f.Manufacturer;
        m.ColorName = f.ColorName;
        m.ColorCode = f.ColorCode;
        m.Comment = f.Comment;
        if (request.IsActive.HasValue)
            m.IsActive = request.IsActive.Value;

        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (IsUniqueViolation(e)) { return Conflict("Такой материал уже есть."); }
        return NoContent();
    }

    /// <summary>
    /// Удалить материал. Физическое удаление — исключение из мягкого удаления:
    /// у материала без ссылок (рецептов) нет истории.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var m = await _context.Materials.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null)
            return NotFound();
        if ((await UsedMaterialIdsAsync(new[] { id })).Count > 0)
            return Conflict("Материал используется в рецептах — его можно только отправить в архив");

        _context.Materials.Remove(m);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation })
        {
            // Гонка: материал использовали между проверкой и удалением.
            return Conflict("Материал используется в рецептах — его можно только отправить в архив");
        }
        return NoContent();
    }

    /// <summary>Ранее введённые производители — по обоим типам, включая архивные материалы.</summary>
    [HttpGet("suggestions/manufacturers")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<string>>> SuggestManufacturers([FromQuery] string? search = null)
    {
        var query = _context.Materials.Where(m => m.Manufacturer != null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.Manufacturer!, pattern));
        }
        var list = await query.Select(m => m.Manufacturer!).Distinct()
            .OrderBy(x => x).Take(SuggestionLimit).ToListAsync();
        return Ok(list);
    }

    /// <summary>Ранее введённые цвета (наименование + код) — по обоим типам, включая архивные материалы.</summary>
    [HttpGet("suggestions/colors")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<MaterialColorSuggestionDto>>> SuggestColors([FromQuery] string? search = null)
    {
        var query = _context.Materials.Where(m => m.ColorName != null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.ColorName!, pattern)
                || (m.ColorCode != null && EF.Functions.ILike(m.ColorCode, pattern)));
        }
        var list = await query.Select(m => new { Name = m.ColorName!, Code = m.ColorCode }).Distinct()
            .OrderBy(x => x.Name).ThenBy(x => x.Code).Take(SuggestionLimit).ToListAsync();
        return Ok(list.Select(x => new MaterialColorSuggestionDto { Name = x.Name, Code = x.Code }).ToList());
    }
}
```

- [ ] **Step 5: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Materials"`
Expected: все PASS (в том числе тесты Task 2).

- [ ] **Step 6: Коммит**

```bash
git add Wintime.Control.Core/DTOs/Material Wintime.Control.API/Controllers/MaterialsController.cs Wintime.Control.Tests.Integration/Materials/MaterialsControllerTests.cs
git commit -m "feat(materials): MaterialsController — CRUD, archive, suggestions (PZP-22)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Фронт — клиент API и общий `SuggestInput`

**Files:**
- Create: `Wintime-Control-Frontend/src/api/materials.js`
- Create: `Wintime-Control-Frontend/src/components/common/SuggestInput.vue`
- Test: `Wintime-Control-Frontend/src/components/common/__tests__/SuggestInput.spec.js`

**Interfaces:**
- Consumes: API Task 3.
- Produces:
  - `materialsApi = { getList(params), getById(id), create(data), update(id, data), remove(id), suggestManufacturers(search), suggestColors(search) }` (axios-промисы `{ data }`; `apiClient` уже добавляет префикс `/api`)
  - `<SuggestInput v-model="string" :fetch="(search) => Promise<item[]>" :item-label="(item) => string" :item-value="(item) => string" placeholder="..." @select="(item) => …" />`
    - `fetch` возвращает массив произвольных элементов (строк или объектов);
    - `itemLabel` — текст пункта списка (по умолчанию `String(item)`);
    - `itemValue` — что записать в `v-model` при выборе (по умолчанию `String(item)`);
    - ввод значения не из списка разрешён (`v-model` обновляется на каждый ввод);
    - ошибка `fetch` → пустой список, без исключения.

- [ ] **Step 1: Клиент API**

`Wintime-Control-Frontend/src/api/materials.js`:

```js
import apiClient from './client'

// Справочник материалов — пластики и красители (PZP-22).
export const materialsApi = {
  getList(params) {
    return apiClient.get('/materials', { params })
  },
  getById(id) {
    return apiClient.get(`/materials/${id}`)
  },
  create(data) {
    return apiClient.post('/materials', data)
  },
  update(id, data) {
    return apiClient.put(`/materials/${id}`, data)
  },
  remove(id) {
    return apiClient.delete(`/materials/${id}`)
  },
  suggestManufacturers(search) {
    return apiClient.get('/materials/suggestions/manufacturers', { params: { search } })
  },
  suggestColors(search) {
    return apiClient.get('/materials/suggestions/colors', { params: { search } })
  }
}
```

- [ ] **Step 2: Написать падающие тесты**

`Wintime-Control-Frontend/src/components/common/__tests__/SuggestInput.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import SuggestInput from '../SuggestInput.vue'

const mounted = []
function mountInput(props) {
  const w = mount(SuggestInput, {
    props: { modelValue: '', ...props },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
  mounted.push(w)
  return w
}

afterEach(() => {
  mounted.splice(0).forEach(w => w.unmount())
  document.body.innerHTML = ''
})

describe('SuggestInput', () => {
  it('запрашивает подсказки через fetch и показывает подписи', async () => {
    const fetch = vi.fn().mockResolvedValue([{ name: 'Чёрный', code: 'RAL 9005' }])
    const w = mountInput({ fetch, itemLabel: c => `${c.name} · ${c.code}`, itemValue: c => c.name })

    const cb = vi.fn()
    await w.vm.querySearch('чёр', cb)
    await flushPromises()

    expect(fetch).toHaveBeenCalledWith('чёр')
    expect(cb).toHaveBeenCalledWith([{ value: 'Чёрный', label: 'Чёрный · RAL 9005', item: { name: 'Чёрный', code: 'RAL 9005' } }])
  })

  it('строки работают без itemLabel/itemValue', async () => {
    const w = mountInput({ fetch: vi.fn().mockResolvedValue(['Сибур']) })
    const cb = vi.fn()
    await w.vm.querySearch('', cb)
    expect(cb).toHaveBeenCalledWith([{ value: 'Сибур', label: 'Сибур', item: 'Сибур' }])
  })

  it('ошибка fetch даёт пустой список', async () => {
    const w = mountInput({ fetch: vi.fn().mockRejectedValue(new Error('сеть')) })
    const cb = vi.fn()
    await w.vm.querySearch('x', cb)
    expect(cb).toHaveBeenCalledWith([])
  })

  it('свободный ввод обновляет v-model', async () => {
    const w = mountInput({ fetch: vi.fn().mockResolvedValue([]) })
    await w.find('input').setValue('Новый завод')
    expect(w.emitted('update:modelValue').at(-1)).toEqual(['Новый завод'])
  })

  it('выбор пункта пишет value в v-model и отдаёт исходный элемент в select', async () => {
    const item = { name: 'Белый', code: 'RAL 9010' }
    const w = mountInput({ fetch: vi.fn(), itemValue: c => c.name })
    w.vm.onSelect({ value: 'Белый', label: 'Белый · RAL 9010', item })
    expect(w.emitted('update:modelValue').at(-1)).toEqual(['Белый'])
    expect(w.emitted('select').at(-1)).toEqual([item])
  })
})
```

- [ ] **Step 3: Убедиться, что тесты падают**

Run (в `Wintime-Control-Frontend`): `npx vitest run src/components/common/__tests__/SuggestInput.spec.js`
Expected: FAIL — `SuggestInput.vue` не найден.

- [ ] **Step 4: Реализация**

`Wintime-Control-Frontend/src/components/common/SuggestInput.vue`:

```vue
<template>
  <el-autocomplete
    :model-value="modelValue"
    :fetch-suggestions="querySearch"
    :placeholder="placeholder"
    :trigger-on-focus="true"
    clearable
    class="w-full"
    @update:model-value="v => emit('update:modelValue', v ?? '')"
    @select="onSelect"
  >
    <template #default="{ item }">
      <span>{{ item.label }}</span>
    </template>
  </el-autocomplete>
</template>

<script setup>
// Свободный текст с подсказками ранее введённых значений (PZP-22; переиспользуют PZP-23, PZP-28).
// Значение не из списка разрешено: подсказки только помогают не плодить разные написания.
const props = defineProps({
  modelValue: { type: String, default: '' },
  fetch: { type: Function, required: true },          // (search) => Promise<item[]>
  itemLabel: { type: Function, default: item => String(item) },
  itemValue: { type: Function, default: item => String(item) },
  placeholder: { type: String, default: '' }
})

const emit = defineEmits(['update:modelValue', 'select'])

async function querySearch(search, cb) {
  try {
    const items = await props.fetch(search ?? '')
    cb((items ?? []).map(item => ({ value: props.itemValue(item), label: props.itemLabel(item), item })))
  } catch {
    cb([])
  }
}

function onSelect(suggestion) {
  emit('update:modelValue', suggestion.value)
  emit('select', suggestion.item)
}

defineExpose({ querySearch, onSelect })
</script>
```

- [ ] **Step 5: Тесты проходят**

Run: `npx vitest run src/components/common/__tests__/SuggestInput.spec.js`
Expected: 5 PASS.

- [ ] **Step 6: Коммит**

```bash
git add Wintime-Control-Frontend/src/api/materials.js Wintime-Control-Frontend/src/components/common/SuggestInput.vue Wintime-Control-Frontend/src/components/common/__tests__/SuggestInput.spec.js
git commit -m "feat(materials-ui): materials API client and shared SuggestInput (PZP-22)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Фронт — `MaterialDictionary.vue`, маршруты и меню

**Files:**
- Create: `Wintime-Control-Frontend/src/views/dictionary/MaterialDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js` (в `children` маршрута `dictionary`, после `product-types`)
- Modify: `Wintime-Control-Frontend/src/layouts/DefaultLayout.vue` (после пункта «Типы изделий», ~строка 83)
- Test: `Wintime-Control-Frontend/src/views/dictionary/__tests__/MaterialDictionary.spec.js`

**Interfaces:**
- Consumes: `materialsApi` и `SuggestInput` (Task 4); `apiErrorMessage` из `@/utils/apiError`.
- Produces: компонент с пропом `materialType: 'Plastic' | 'Colorant'` (маршрут передаёт его через `props: { materialType: '…' }`); маршруты `/dictionary/plastics` (`DictionaryPlastics`), `/dictionary/colorants` (`DictionaryColorants`).

Тексты по типу:

| | Plastic | Colorant |
|---|---|---|
| Заголовок | Справочник пластиков | Справочник красителей |
| Кнопка | Добавить пластик | Добавить краситель |
| Диалог | Новый пластик / Редактирование пластика | Новый краситель / Редактирование красителя |

- [ ] **Step 1: Написать падающие тесты**

`Wintime-Control-Frontend/src/views/dictionary/__tests__/MaterialDictionary.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage, ElMessageBox } from 'element-plus'
import MaterialDictionary from '../MaterialDictionary.vue'

vi.mock('@/api/materials', () => ({
  materialsApi: {
    getList: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(() => Promise.resolve({ data: {} })),
    update: vi.fn(() => Promise.resolve({})),
    remove: vi.fn(() => Promise.resolve({})),
    suggestManufacturers: vi.fn(() => Promise.resolve({ data: [] })),
    suggestColors: vi.fn(() => Promise.resolve({ data: [] }))
  }
}))

import { materialsApi } from '@/api/materials'

const plastic = { id: 'm1', type: 'Plastic', article: 'PP-01', name: 'Полипропилен', manufacturer: 'Сибур',
  colorName: null, colorCode: null, comment: null, isActive: true, isUsed: false }
const usedPlastic = { ...plastic, id: 'm2', article: 'PP-02', name: 'ПП-2', isUsed: true }

const mounted = []
function mountView(materialType = 'Plastic') {
  const w = mount(MaterialDictionary, {
    props: { materialType },
    global: { plugins: [ElementPlus], stubs: { Plus: true } },
    attachTo: document.body
  })
  mounted.push(w)
  return w
}

describe('MaterialDictionary (PZP-22)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    materialsApi.getList.mockResolvedValue({ data: [plastic, usedPlastic] })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    mounted.splice(0).forEach(w => w.unmount())
    document.body.innerHTML = ''
  })

  it('пластики: заголовок, кнопка и запрос списка по типу', async () => {
    const w = mountView('Plastic')
    await flushPromises()
    expect(w.text()).toContain('Справочник пластиков')
    expect(w.text()).toContain('Добавить пластик')
    expect(materialsApi.getList).toHaveBeenCalledWith(expect.objectContaining({ type: 'Plastic' }))
  })

  it('красители: свой заголовок и тип в запросе', async () => {
    const w = mountView('Colorant')
    await flushPromises()
    expect(w.text()).toContain('Справочник красителей')
    expect(materialsApi.getList).toHaveBeenCalledWith(expect.objectContaining({ type: 'Colorant' }))
  })

  it('кнопка «Удалить» только у неиспользованных', async () => {
    const w = mountView()
    await flushPromises()
    expect(w.findAll('.material-delete')).toHaveLength(1)
  })

  it('артикул переводится в верхний регистр на лету', async () => {
    const w = mountView()
    await flushPromises()
    w.vm.showCreateModal()
    await flushPromises()
    w.vm.form.article = 'pp-03'
    w.vm.onArticleInput()
    expect(w.vm.form.article).toBe('PP-03')
  })

  it('краситель без цвета не сохраняется', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const w = mountView('Colorant')
    await flushPromises()
    w.vm.showCreateModal()
    Object.assign(w.vm.form, { article: 'C-01', name: 'Краситель', colorName: '' })
    await w.vm.save()
    expect(materialsApi.create).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledWith('Для красителя укажите цвет')
  })

  it('пластик без цвета сохраняется с типом и нормализованным артикулом', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    const w = mountView('Plastic')
    await flushPromises()
    w.vm.showCreateModal()
    Object.assign(w.vm.form, { article: ' pp-09 ', name: ' Полиэтилен ', manufacturer: 'Сибур' })
    await w.vm.save()
    expect(materialsApi.create).toHaveBeenCalledWith(expect.objectContaining({
      type: 'Plastic', article: 'PP-09', name: 'Полиэтилен', manufacturer: 'Сибур'
    }))
  })

  it('правка отправляет пустые строки для очищенных полей и не отправляет тип', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    const w = mountView('Plastic')
    await flushPromises()
    w.vm.editItem(plastic)
    w.vm.form.manufacturer = ''
    await w.vm.save()
    const [id, payload] = materialsApi.update.mock.calls[0]
    expect(id).toBe('m1')
    expect(payload.manufacturer).toBe('')
    expect(payload).not.toHaveProperty('type')
  })

  it('выбор цвета из подсказки заполняет наименование и код', async () => {
    const w = mountView('Colorant')
    await flushPromises()
    w.vm.showCreateModal()
    w.vm.onColorSelect({ name: 'Чёрный', code: 'RAL 9005' })
    expect(w.vm.form.colorName).toBe('Чёрный')
    expect(w.vm.form.colorCode).toBe('RAL 9005')
  })

  it('архив отправляет isActive=false через update', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    const w = mountView()
    await flushPromises()
    await w.vm.toggleArchive(plastic)
    expect(materialsApi.update).toHaveBeenCalledWith('m1', { isActive: false })
  })

  it('удаление после подтверждения', async () => {
    vi.spyOn(ElMessage, 'success').mockImplementation(() => {})
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm')
    const w = mountView()
    await flushPromises()
    await w.vm.deleteItem(plastic)
    expect(materialsApi.remove).toHaveBeenCalledWith('m1')
  })
})
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `npx vitest run src/views/dictionary/__tests__/MaterialDictionary.spec.js`
Expected: FAIL — `MaterialDictionary.vue` не найден.

- [ ] **Step 3: Реализация компонента**

`Wintime-Control-Frontend/src/views/dictionary/MaterialDictionary.vue`:

```vue
<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">{{ texts.title }}</h2>
        <p class="text-gray-600 mt-1">{{ texts.subtitle }}</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        {{ texts.add }}
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Артикул, наименование, производитель, цвет" clearable />
        </el-form-item>
        <el-form-item label="Статус">
          <el-select v-model="filters.isActive" placeholder="Все" clearable style="width: 160px;">
            <el-option label="Активные" :value="true" />
            <el-option label="Не активные" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="loadItems">Применить</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-table :data="items" stripe style="width: 100%" v-loading="loading" row-key="id">
      <el-table-column prop="article" label="Артикул" width="160" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column prop="manufacturer" label="Производитель" width="200" />
      <el-table-column label="Цвет" width="220">
        <template #default="{ row }">{{ colorLabel(row) }}</template>
      </el-table-column>
      <el-table-column label="Статус" width="120" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">{{ row.isActive ? 'Активен' : 'В архиве' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="300" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editItem(row)">Редактировать</el-button>
          <el-button size="small" :type="row.isActive ? 'danger' : 'success'" @click="toggleArchive(row)">
            {{ row.isActive ? 'В архив' : 'Вернуть' }}
          </el-button>
          <el-button v-if="!row.isUsed" class="material-delete" size="small" type="danger" plain @click="deleteItem(row)">
            Удалить
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="dialogVisible"
      :title="editing ? texts.editTitle : texts.createTitle"
      width="560px"
    >
      <el-form :model="form" label-width="140px">
        <el-form-item label="Артикул" required>
          <el-input v-model="form.article" placeholder="PP-01" @input="onArticleInput" />
        </el-form-item>
        <el-form-item label="Наименование" required>
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="Производитель">
          <SuggestInput v-model="form.manufacturer" :fetch="fetchManufacturers" />
        </el-form-item>
        <el-form-item label="Цвет" :required="isColorant">
          <SuggestInput
            v-model="form.colorName"
            :fetch="fetchColors"
            :item-label="colorLabel"
            :item-value="c => c.name"
            placeholder="Наименование"
            @select="onColorSelect"
          />
        </el-form-item>
        <el-form-item label="Код цвета">
          <el-input v-model="form.colorCode" placeholder="RAL 9005" />
        </el-form-item>
        <el-form-item label="Комментарий">
          <el-input v-model="form.comment" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">Отмена</el-button>
        <el-button type="primary" @click="save" :loading="saving">Сохранить</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { materialsApi } from '@/api/materials'
import { apiErrorMessage } from '@/utils/apiError'
import SuggestInput from '@/components/common/SuggestInput.vue'

// Справочник материалов (PZP-22): один компонент для пластиков и красителей, тип — из маршрута.
const props = defineProps({
  materialType: { type: String, required: true }   // 'Plastic' | 'Colorant'
})

const TEXTS = {
  Plastic: {
    title: 'Справочник пластиков', subtitle: 'Пластики — основа рецептов изделий', add: 'Добавить пластик',
    createTitle: 'Новый пластик', editTitle: 'Редактирование пластика', one: 'Пластик'
  },
  Colorant: {
    title: 'Справочник красителей', subtitle: 'Красители для рецептов изделий', add: 'Добавить краситель',
    createTitle: 'Новый краситель', editTitle: 'Редактирование красителя', one: 'Краситель'
  }
}

// Текст ошибки сервера; нестроковое тело (ProblemDetails и т.п.) заменяется запасным текстом.
function errText(error, fallback) {
  const msg = apiErrorMessage(error, fallback)
  return typeof msg === 'string' && msg ? msg : fallback
}

const texts = computed(() => TEXTS[props.materialType])
const isColorant = computed(() => props.materialType === 'Colorant')

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref(null)
const items = ref([])

const filters = reactive({ search: '', isActive: null })
const emptyForm = () => ({ article: '', name: '', manufacturer: '', colorName: '', colorCode: '', comment: '' })
const form = reactive(emptyForm())

onMounted(loadItems)
// Переход «Пластики» ↔ «Красители» переиспользует экземпляр компонента — перезагрузить список.
watch(() => props.materialType, loadItems)

function colorLabel(c) {
  const name = c.colorName ?? c.name
  const code = c.colorCode ?? c.code
  return [name, code].filter(Boolean).join(' · ')
}

async function loadItems() {
  loading.value = true
  try {
    const { data } = await materialsApi.getList({
      type: props.materialType,
      isActive: filters.isActive,
      search: filters.search
    })
    items.value = data
  } catch {
    ElMessage.error('Ошибка загрузки материалов')
  } finally {
    loading.value = false
  }
}

const fetchManufacturers = async (search) => (await materialsApi.suggestManufacturers(search)).data
const fetchColors = async (search) => (await materialsApi.suggestColors(search)).data

function onArticleInput() {
  form.article = form.article.toUpperCase()
}

function onColorSelect(c) {
  form.colorName = c.name
  form.colorCode = c.code ?? ''
}

function showCreateModal() {
  editing.value = null
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

function editItem(row) {
  editing.value = row
  Object.assign(form, {
    article: row.article, name: row.name, manufacturer: row.manufacturer ?? '',
    colorName: row.colorName ?? '', colorCode: row.colorCode ?? '', comment: row.comment ?? ''
  })
  dialogVisible.value = true
}

async function save() {
  const payload = {
    article: form.article.trim().toUpperCase(),
    name: form.name.trim(),
    // Пустая строка — «очистить поле» (PUT частичный: null значил бы «не менять»).
    manufacturer: form.manufacturer.trim(),
    colorName: form.colorName.trim(),
    colorCode: form.colorCode.trim(),
    comment: form.comment.trim()
  }
  if (!payload.article || !payload.name) {
    ElMessage.error('Введите артикул и наименование')
    return
  }
  if (isColorant.value && !payload.colorName) {
    ElMessage.error('Для красителя укажите цвет')
    return
  }
  saving.value = true
  try {
    if (editing.value) {
      await materialsApi.update(editing.value.id, payload)
      ElMessage.success(`${texts.value.one} обновлён`)
    } else {
      await materialsApi.create({ type: props.materialType, ...payload })
      ElMessage.success(`${texts.value.one} добавлен`)
    }
    dialogVisible.value = false
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка сохранения материала'))
    return
  } finally {
    saving.value = false
  }
  await loadItems()
}

async function toggleArchive(row) {
  try {
    await materialsApi.update(row.id, { isActive: !row.isActive })
    ElMessage.success(row.isActive ? `${texts.value.one} в архиве` : `${texts.value.one} возвращён`)
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка изменения статуса'))
    return
  }
  await loadItems()
}

async function deleteItem(row) {
  try {
    await ElMessageBox.confirm(`Удалить ${texts.value.one.toLowerCase()} ${row.article}?`, 'Подтверждение', {
      confirmButtonText: 'Удалить', cancelButtonText: 'Отмена', type: 'warning'
    })
  } catch {
    return
  }
  try {
    await materialsApi.remove(row.id)
    ElMessage.success(`${texts.value.one} удалён`)
  } catch (error) {
    ElMessage.error(errText(error, 'Ошибка удаления материала'))
    return
  }
  await loadItems()
}

defineExpose({ form, showCreateModal, editItem, save, toggleArchive, deleteItem, onArticleInput, onColorSelect })
</script>
```

- [ ] **Step 4: Тесты компонента проходят**

Run: `npx vitest run src/views/dictionary/__tests__/MaterialDictionary.spec.js`
Expected: 10 PASS.

- [ ] **Step 5: Маршруты и меню**

`src/router/index.js` — в `children` маршрута `dictionary` сразу после объекта `product-types`:

```js
          {
            path: 'plastics',
            name: 'DictionaryPlastics',
            component: () => import('@/views/dictionary/MaterialDictionary.vue'),
            props: { materialType: 'Plastic' },
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник пластиков' }
          },
          {
            path: 'colorants',
            name: 'DictionaryColorants',
            component: () => import('@/views/dictionary/MaterialDictionary.vue'),
            props: { materialType: 'Colorant' },
            meta: { roles: ['Admin', 'Manager'], title: 'Справочник красителей' }
          },
```

`src/layouts/DefaultLayout.vue` — после `<el-menu-item index="/dictionary/product-types">Типы изделий</el-menu-item>`:

```vue
            <el-menu-item index="/dictionary/plastics">Пластики</el-menu-item>
            <el-menu-item index="/dictionary/colorants">Красители</el-menu-item>
```

- [ ] **Step 6: Весь фронт зелёный и собирается**

Run (в `Wintime-Control-Frontend`): `npx vitest run` и затем `npm run build`
Expected: все тесты PASS; сборка без ошибок. Если какой-то существующий тест проверяет точный список пунктов меню или маршрутов — обновить его под два новых пункта.

- [ ] **Step 7: Коммит**

```bash
git add Wintime-Control-Frontend/src/views/dictionary/MaterialDictionary.vue Wintime-Control-Frontend/src/views/dictionary/__tests__/MaterialDictionary.spec.js Wintime-Control-Frontend/src/router/index.js Wintime-Control-Frontend/src/layouts/DefaultLayout.vue
git commit -m "feat(materials-ui): plastics and colorants dictionaries (PZP-22)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Если на шаге 6 правились существующие тесты — добавить и их пути.)

---

### Task 6: Документация, CLAUDE.md, версия, полный прогон

**Files:**
- Create: `docs/domain/material.md`
- Modify: `docs/domain/README.md` (словарь + каталог)
- Modify: `CLAUDE.md` (раздел «Инварианты домена»)
- Modify: `docs/user-guide/manager.md` (раздел 9, после 9.4; содержание)
- Modify: `VERSION` → `1.20.0`

- [ ] **Step 1: `docs/domain/material.md`**

```markdown
# Материал (`Material`)

> Сырьё для литья — пластик или краситель. Справочник, на который ссылаются рецепты изделий (BL-36).

## Что это

Позиция справочника сырья: гранулированный пластик (основа отливки) или краситель (суперконцентрат,
пигмент). Справочник ведут менеджер и администратор; рецепт изделия (BL-36) составляется из
материалов этого справочника. Остатки сырья не ведутся — система не складская.

## Код

- Сущность/enum: `Wintime.Control.Core.Entities.Material`, `Wintime.Control.Core.Enums.MaterialType`
- Правила: `Core/Policies/MaterialRules` (нормализация, обязательные поля, длины)
- API: `MaterialsController` (`/api/materials`, подсказки — `/api/materials/suggestions/*`)
- UI: `views/dictionary/MaterialDictionary.vue` (меню «Справочники → Пластики / Красители»)

## Ключевые поля

| Поле | Смысл и правила |
|---|---|
| `Type` | `Plastic` (пластик) или `Colorant` (краситель). Задаётся при создании и не меняется |
| `Article` | Артикул, обязателен. Trim + заглавные. Уникален внутри типа |
| `Name` | Наименование, обязательно |
| `Manufacturer` | Производитель, необязателен. Пара (производитель, наименование) уникальна внутри типа без учёта регистра; пустой производитель — одно значение |
| `ColorName` | Наименование цвета. Обязательно у красителя |
| `ColorCode` | Код цвета (RAL, Pantone, HEX), необязателен; регистр сохраняется |
| `Comment` | Произвольный комментарий |
| `IsActive` | Архивный флаг. Архивный материал нельзя выбрать в новом рецепте; ссылки сохраняются |

## Инварианты (нарушение = баг)

- Тип материала не меняется: рецепт допускает замену ингредиента только материалом того же типа (BL-36).
- Уникальность — внутри типа, без учёта регистра: (тип, артикул) и (тип, производитель, наименование).
  Пластик и краситель могут иметь одинаковый артикул.
- Физически удаляется только материал без ссылок (рецепты изделий и их копии в заданиях, включая
  архивные) — исключение из мягкого удаления. → [CLAUDE.md](../../CLAUDE.md)

## Связанные

- Спека: `docs/superpowers/specs/2026-11-01-pzp-22-materials-design.md`
- Сущности домена: [`product-type.md`](product-type.md)
```

- [ ] **Step 2: `docs/domain/README.md`**

В таблицу словаря после строки «Изделие»:

```markdown
| Материал (пластик, краситель) | `Material` (`MaterialType`) | Сырьё для литья; ингредиент рецепта изделия |
```

В «Каталог сущностей» после строки `product-type.md`:

```markdown
- ✅ [`material.md`](material.md) — Материал (`Material`): пластики и красители, уникальность внутри типа, удаление
```

- [ ] **Step 3: CLAUDE.md**

В разделе «Инварианты домена» сразу после подраздела «Тип изделия и изделие (`ProductType` → `Product`)» добавить:

```markdown
### Материалы (`Material`)

Справочник сырья — одна таблица, тип `MaterialType` (`Plastic`/`Colorant`); в UI два справочника.
Тип задаётся при создании и **не меняется**. Уникальность внутри типа без учёта регистра:
`(Type, Article)` и `(Type, Manufacturer, Name)` (пустой производитель — одно значение; индекс на
выражении в миграции `AddMaterials`). У красителя обязателен цвет. Физически удаляется только
материал без ссылок (рецепты и их копии в заданиях) — второе исключение из мягкого удаления.
```

И в подразделе «`IsActive` — архивный флаг» последнее предложение абзаца «Никогда не удалять сущности физически…» дополнить так, чтобы перечислить исключения. Заменить:

```markdown
Никогда не удалять сущности физически — только `IsActive = false`.
```

на:

```markdown
Никогда не удалять сущности физически — только `IsActive = false` (исключения: изделие и материал без ссылок, см. ниже).
```

- [ ] **Step 4: Руководство менеджера**

Прочитать `docs/user-guide/manager.md` — раздел «9.4. Типы изделий» и соседние, чтобы повторить стиль. После раздела 9.4 (перед 9.5) вставить подраздел и сдвинуть номера последующих подразделов раздела 9 и их ссылки в «Содержании» на +1:

```markdown
### 9.5. Пластики и красители

**Справочники → Пластики** и **Справочники → Красители** — сырьё для рецептов изделий. Оба
справочника устроены одинаково.

- **Добавить:** кнопка «Добавить пластик» («Добавить краситель»). Обязательны артикул и
  наименование, у красителя — ещё цвет. Артикул автоматически пишется заглавными буквами.
- **Производитель и цвет** — можно выбрать из подсказки (ранее введённые значения, в том числе у
  архивных материалов) или ввести новое. Выбор цвета из подсказки заполняет и наименование, и код.
- **Повторы не допускаются:** в одном справочнике нельзя завести два материала с одинаковым
  артикулом или с одинаковыми производителем и наименованием (регистр букв не важен).
- **В архив / Вернуть** — архивный материал нельзя выбрать в новый рецепт, но он остаётся в старых.
- **Удалить** — доступно, пока материал не используется ни в одном рецепте. Ошибочно заведённую
  запись удаляйте, используемую — отправляйте в архив.
```

- [ ] **Step 5: Версия**

`VERSION`: заменить `1.19.0` на `1.20.0`.

- [ ] **Step 6: Полный прогон**

Run (из корня): `dotnet build Wintime.Control.sln` → `dotnet test Wintime.Control.Tests.Unit` → `dotnet test Wintime.Control.Tests.Integration`
Run (в `Wintime-Control-Frontend`): `npx vitest run`
Expected: сборка без ошибок, все тесты PASS. Если падают тесты, не связанные с материалами, — зафиксировать их имена и проверить, падают ли они на `master` (`git stash` не использовать — посторонний изменённый файл; достаточно сравнить с выводом прогона на master в отдельном worktree или сообщить контроллеру).

- [ ] **Step 7: Коммит**

```bash
git add docs/domain/material.md docs/domain/README.md CLAUDE.md docs/user-guide/manager.md VERSION
git commit -m "docs(materials): domain page, CLAUDE.md invariant, manager guide, v1.20.0 (PZP-22)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
