# PZP-09 — Справочник изделий (ProductType) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ввести сущность-каталог изделий `ProductType` со связью `ProductType → Mold` (1:N) и правилами атрибуции выпуска на тип изделия — фундамент под заказы (PZP-05) и партии (BL-27).

**Architecture:** Аддитивная доменная сущность `: BaseEntity` с мягким удалением (`IsActive`) по образцу `DowntimeReason`/`Mold`. CRUD-контроллер по образцу `MoldsController` (ролевой доступ, ручной маппинг DTO, уникальность через 409). Связь с `Mold` — nullable FK + бизнес-правила в `MoldsController`/`TasksController`. Фронт — справочник по образцу `MoldDictionary.vue` + обязательный селект типа в форме ПФ.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql (PostgreSQL 16), xUnit + FluentAssertions + Testcontainers (интеграционные тесты), Vue 3 + Element Plus + Vite.

**Спека:** `docs/superpowers/specs/2026-07-25-product-type-catalog-design.md` · **ADR:** `docs/adr/0007-product-type-catalog.md`

## Global Constraints

- **Роль — только `User.Role`** (enum `UserRole`). Роли эндпоинтов: чтение — `Admin,Manager,Adjuster`; запись — `Admin,Manager`. Строки ролей — через `Wintime.Control.Shared.Constants.Roles`.
- **`IsActive` — архивный флаг** (мягкое удаление). Сущности физически не удалять — только `IsActive = false`. У `ProductType` **нет** DELETE-эндпоинта.
- **DateTime → PostgreSQL:** все `DateTime` в EF-запросах обязаны иметь `Kind=Utc`. (В этой задаче новых дат нет — `CreatedAt` ставит `BaseEntity`.)
- **`Article` уникален**, exact match после `.Trim()`, конфликт → `409 Conflict`; проверка на POST и PUT (PUT исключает себя `Id != id`). Авторитет валидации — бэкенд.
- **net9.0**, ручной маппинг DTO (без AutoMapper), паттерн существующих контроллеров.
- **Фронтенд:** в проекте нет harness для mount-тестов `.vue` (нет `@vue/test-utils`/jsdom). `.vue`-изменения проверяются ручным smoke; автотесты — только backend (xUnit).

**Ветка:** `feature/pzp-09-product-type-catalog` (уже создана, дизайн-доки закоммичены).

---

### Task 1: Доменная сущность `ProductType` + связь с `Mold` + миграция + сид тестфабрики

**Files:**
- Create: `Wintime.Control.Core/Entities/ProductType.cs`
- Modify: `Wintime.Control.Core/Entities/Mold.cs` (добавить `ProductTypeId` + навигацию)
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` (DbSet + конфиг + FK)
- Create (генерируется): `Wintime.Control.Infrastructure/Migrations/*_AddProductType.cs`
- Modify: `Wintime.Control.Tests.Integration/Infrastructure/IntegrationTestFactory.cs` (сид `ProductType`, привязка `TestMoldId`)
- Test: `Wintime.Control.Tests.Integration/ProductTypes/ProductTypeEntityTests.cs`

**Interfaces:**
- Produces: `ProductType { Guid Id; string Article; string Name; bool IsActive; ICollection<Mold> Molds }`; `Mold.ProductTypeId (Guid?)`, `Mold.ProductType (ProductType?)`; `ControlDbContext.ProductTypes`; `IntegrationTestFactory.TestProductTypeId (Guid)`.

- [ ] **Step 1: Написать сущность `ProductType`**

Create `Wintime.Control.Core/Entities/ProductType.cs`:

```csharp
namespace Wintime.Control.Core.Entities;

/// <summary>
/// Тип изделия / номенклатурная позиция. Каталог-фундамент под заказы (PZP-05)
/// и партии/паспорт (BL-27). См. ADR-0007.
/// </summary>
public class ProductType : BaseEntity
{
    public string Article { get; set; } = string.Empty; // уникальный человекочитаемый артикул (CRM/1С)
    public string Name { get; set; } = string.Empty;    // наименование изделия
    public bool IsActive { get; set; } = true;          // архивный флаг (мягкое удаление)

    // Navigation
    public ICollection<Mold> Molds { get; set; } = new List<Mold>();
}
```

- [ ] **Step 2: Добавить FK на `Mold`**

In `Wintime.Control.Core/Entities/Mold.cs`, после свойства `MoldStatus` (перед секцией `// Navigation`) добавить:

```csharp
    // Тип изделия (PZP-09, ADR-0007). Nullable — старые ПФ без типа; для новых обязателен на уровне API.
    public Guid? ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
```

- [ ] **Step 3: Зарегистрировать в `ControlDbContext`**

In `ControlDbContext.cs`, добавить DbSet рядом с остальными (после `public DbSet<ImmCycle> ImmCycles { get; set; }`):

```csharp
    public DbSet<ProductType> ProductTypes { get; set; }
```

В `OnModelCreating`, в блок `builder.Entity<Mold>(...)` добавить связь, и отдельно сконфигурировать `ProductType`. Заменить существующий блок Mold:

```csharp
        // Конфигурация Mold
        builder.Entity<Mold>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FormId).IsUnique();
            entity.HasOne(e => e.ProductType)
                  .WithMany(pt => pt.Molds)
                  .HasForeignKey(e => e.ProductTypeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Конфигурация ProductType (PZP-09)
        builder.Entity<ProductType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Article).IsUnique();
        });
        builder.Entity<ProductType>().ToTable("ProductTypes");
```

- [ ] **Step 4: Создать миграцию**

Run:
```powershell
dotnet ef migrations add AddProductType --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: создаётся `Migrations/<timestamp>_AddProductType.cs` с `CreateTable("ProductTypes")`, колонкой `Molds.ProductTypeId` (nullable), unique-индексом на `ProductTypes.Article`, индексом+FK на `Molds.ProductTypeId` (onDelete SetNull). Открыть файл и глазами проверить, что нет лишних изменений (только эти).

- [ ] **Step 5: Сид `ProductType` в тестфабрике + привязка `TestMoldId`**

In `IntegrationTestFactory.cs`, добавить публичное свойство рядом с `TestMoldId`:

```csharp
    public Guid TestProductTypeId { get; } = Guid.NewGuid();
```

В `SeedTestEntitiesAsync`, **перед** блоком `if (!db.Molds.Any(...))`, добавить сид типа:

```csharp
        if (!db.ProductTypes.Any(p => p.Id == TestProductTypeId))
        {
            db.ProductTypes.Add(new ProductType
            {
                Id       = TestProductTypeId,
                Article  = "TYPE-TEST",
                Name     = "Test Product",
                IsActive = true
            });
        }
```

И в существующем сид-блоке `Mold` добавить привязку (иначе guard из Task 4 сломает тесты задач):

```csharp
        if (!db.Molds.Any(m => m.Id == TestMoldId))
        {
            db.Molds.Add(new Mold
            {
                Id            = TestMoldId,
                Name          = "Test Mold",
                FormId        = "TEST-001",
                Cavities      = 1,
                IsActive      = true,
                ProductTypeId = TestProductTypeId   // ← добавлено
            });
        }
```

- [ ] **Step 6: Написать интеграционный тест сущности**

Create `Wintime.Control.Tests.Integration/ProductTypes/ProductTypeEntityTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductTypeEntityTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ProductTypeEntityTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ProductType_PersistsAndLinksToMold()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var pt = new ProductType { Article = $"ART-{Guid.NewGuid():N}", Name = "Изделие" };
        db.ProductTypes.Add(pt);
        var mold = new Mold
        {
            Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 2,
            IsActive = true, ProductTypeId = pt.Id
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        var loaded = await db.Molds.Include(m => m.ProductType).FirstAsync(m => m.Id == mold.Id);
        loaded.ProductType.Should().NotBeNull();
        loaded.ProductType!.Article.Should().Be(pt.Article);
    }

    [Fact]
    public async Task ProductType_DuplicateArticle_ThrowsOnUniqueIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var article = $"DUP-{Guid.NewGuid():N}";
        db.ProductTypes.Add(new ProductType { Article = article, Name = "A" });
        await db.SaveChangesAsync();

        db.ProductTypes.Add(new ProductType { Article = article, Name = "B" });
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
```

- [ ] **Step 7: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductTypeEntityTests"`
Expected: 2 PASS (миграция применяется автоматически при старте хоста).

- [ ] **Step 8: Убедиться, что существующие тесты не сломались**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TasksControllerTests"`
Expected: все PASS (сид `TestMoldId` теперь с типом → guard из Task 4 ещё не добавлен, но сид уже совместим).

- [ ] **Step 9: Commit**

```powershell
git add Wintime.Control.Core/Entities/ProductType.cs Wintime.Control.Core/Entities/Mold.cs Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations/ Wintime.Control.Tests.Integration/Infrastructure/IntegrationTestFactory.cs Wintime.Control.Tests.Integration/ProductTypes/ProductTypeEntityTests.cs
git commit -m "feat(PZP-09): сущность ProductType + связь Mold→ProductType + миграция"
```

---

### Task 2: DTO + `ProductTypesController` (CRUD справочника)

**Files:**
- Create: `Wintime.Control.Core/DTOs/ProductType/ProductTypeDto.cs`
- Create: `Wintime.Control.Core/DTOs/ProductType/CreateProductTypeRequestDto.cs`
- Create: `Wintime.Control.Core/DTOs/ProductType/UpdateProductTypeRequestDto.cs`
- Create: `Wintime.Control.API/Controllers/ProductTypesController.cs`
- Test: `Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs`

**Interfaces:**
- Consumes: `ControlDbContext.ProductTypes` (Task 1).
- Produces: HTTP `GET/POST/PUT /api/producttypes`; `ProductTypeDto { Guid Id; string Article; string Name; bool IsActive }`; `CreateProductTypeRequestDto { string Article; string Name }`; `UpdateProductTypeRequestDto { string? Article; string? Name; bool? IsActive }`.

- [ ] **Step 1: Написать DTO**

Create `Wintime.Control.Core/DTOs/ProductType/ProductTypeDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.ProductType;

public class ProductTypeDto
{
    public Guid Id { get; set; }
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
```

Create `Wintime.Control.Core/DTOs/ProductType/CreateProductTypeRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.ProductType;

public class CreateProductTypeRequestDto
{
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

Create `Wintime.Control.Core/DTOs/ProductType/UpdateProductTypeRequestDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.ProductType;

public class UpdateProductTypeRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
}
```

- [ ] **Step 2: Написать провальный тест контроллера (CRUD + уникальность + роли + архив)**

Create `Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductTypesControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ProductTypesControllerTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task Manager_CanCreateAndGet()
    {
        var client = await ManagerClientAsync();
        var article = $"ART-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "Крышка" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await create.Content.ReadFromJsonAsync<ProductTypeDto>();
        dto!.Article.Should().Be(article);
        dto.IsActive.Should().BeTrue();

        var get = await client.GetAsync($"/api/producttypes/{dto.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_DuplicateArticle_ReturnsConflict()
    {
        var client = await ManagerClientAsync();
        var article = $"DUP-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "A" });

        var second = await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"  {article.ToUpper()}  ", Name = "B" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_DuplicateArticle_ReturnsConflict_ExcludingSelf()
    {
        var client = await ManagerClientAsync();
        var a1 = $"A1-{Guid.NewGuid():N}";
        var a2 = $"A2-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/producttypes", new CreateProductTypeRequestDto { Article = a1, Name = "X" });
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = a2, Name = "Y" })).Content.ReadFromJsonAsync<ProductTypeDto>();

        // Переименовать второй в артикул первого → конфликт
        var conflict = await client.PutAsJsonAsync($"/api/producttypes/{created!.Id}",
            new UpdateProductTypeRequestDto { Article = a1 });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Тот же артикул у себя → успех (контроллер возвращает 204 NoContent)
        var ok = await client.PutAsJsonAsync($"/api/producttypes/{created.Id}",
            new UpdateProductTypeRequestDto { Article = a2, Name = "Y2" });
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Archive_WithLinkedMold_Succeeds_AndKeepsLink()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"ARCH-{Guid.NewGuid():N}", Name = "Z" }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();

        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 1,
                                  IsActive = true, ProductTypeId = created!.Id };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var archive = await client.PutAsJsonAsync($"/api/producttypes/{created!.Id}",
            new UpdateProductTypeRequestDto { IsActive = false });
        archive.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verify = _factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold2 = await vdb.Molds.FindAsync(moldId);
        mold2!.ProductTypeId.Should().Be(created.Id); // ссылка сохранена
    }

    [Fact]
    public async Task List_FiltersByIsActive()
    {
        var client = await ManagerClientAsync();
        var active = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"ACT-{Guid.NewGuid():N}", Name = "Act" }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();
        var archived = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"ARV-{Guid.NewGuid():N}", Name = "Arv" }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();
        await client.PutAsJsonAsync($"/api/producttypes/{archived!.Id}",
            new UpdateProductTypeRequestDto { IsActive = false });

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>("/api/producttypes?isActive=true");
        list!.Should().Contain(p => p.Id == active!.Id);
        list.Should().NotContain(p => p.Id == archived.Id);
    }

    [Fact]
    public async Task Adjuster_CannotCreate_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"F-{Guid.NewGuid():N}", Name = "N" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Adjuster_CanRead_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.GetAsync("/api/producttypes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 3: Запустить тест — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductTypesControllerTests"`
Expected: FAIL (404 — контроллера ещё нет).

- [ ] **Step 4: Написать `ProductTypesController`**

Create `Wintime.Control.API/Controllers/ProductTypesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductTypesController : ControllerBase
{
    private readonly ControlDbContext _context;

    public ProductTypesController(ControlDbContext context) => _context = context;

    private static ProductTypeDto ToDto(ProductType p) => new()
    {
        Id = p.Id, Article = p.Article, Name = p.Name, IsActive = p.IsActive
    };

    /// <summary>Список типов изделий (фильтры: активность, поиск по артикулу/наименованию).</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<ProductTypeDto>>> GetList(
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        var query = _context.ProductTypes.AsQueryable();

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(p => p.Name.Contains(search) || p.Article.Contains(search));

        var items = await query.OrderBy(p => p.Article).ToListAsync();
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<ProductTypeDto>> GetById(Guid id)
    {
        var pt = await _context.ProductTypes.FindAsync(id);
        return pt == null ? NotFound() : Ok(ToDto(pt));
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ProductTypeDto>> Create([FromBody] CreateProductTypeRequestDto request)
    {
        var article = (request.Article ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(article))
            return BadRequest("Артикул обязателен.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Наименование обязательно.");
        if (await _context.ProductTypes.AnyAsync(p => p.Article.ToLower() == article.ToLower()))
            return Conflict($"Артикул '{article}' уже используется.");

        var pt = new ProductType { Article = article, Name = request.Name.Trim(), IsActive = true };
        _context.ProductTypes.Add(pt);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = pt.Id }, ToDto(pt));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductTypeRequestDto request)
    {
        var pt = await _context.ProductTypes.FindAsync(id);
        if (pt == null)
            return NotFound();

        if (request.Article != null)
        {
            var trimmed = request.Article.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return BadRequest("Артикул не может быть пустым.");
            if (await _context.ProductTypes.AnyAsync(p => p.Article.ToLower() == trimmed.ToLower() && p.Id != id))
                return Conflict($"Артикул '{trimmed}' уже используется.");
            pt.Article = trimmed;
        }
        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Наименование не может быть пустым.");
            pt.Name = request.Name.Trim();
        }
        if (request.IsActive.HasValue)
            pt.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 5: Запустить тесты — убедиться, что проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductTypesControllerTests"`
Expected: все PASS.

> Напоминание: контроллер на PUT возвращает `204 NoContent` (не `200 OK`, как `DowntimeReasonController`, который отдаёт `Ok()`). Тесты выше уже ожидают `NoContent` для успешного PUT — не менять на `OK`.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Core/DTOs/ProductType/ Wintime.Control.API/Controllers/ProductTypesController.cs Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs
git commit -m "feat(PZP-09): ProductTypesController + DTO (CRUD, уникальность Article, архив)"
```

---

### Task 3: Интеграция `Mold` с `ProductType` (DTO, обязательность на create, блокировки на update)

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Mold/MoldDto.cs` (+3 поля)
- Modify: `Wintime.Control.Core/DTOs/Mold/CreateMoldRequestDto.cs` (+ProductTypeId)
- Modify: `Wintime.Control.Core/DTOs/Mold/UpdateMoldRequestDto.cs` (+ProductTypeId)
- Modify: `Wintime.Control.API/Controllers/MoldsController.cs` (populate DTO, валидации UC-4/UC-5/UC-6)
- Test: `Wintime.Control.Tests.Integration/Molds/MoldProductTypeTests.cs`

**Interfaces:**
- Consumes: `ControlDbContext.ProductTypes`, `Mold.ProductTypeId` (Task 1); `IntegrationTestFactory.TestProductTypeId`, `TestMoldId` (Task 1).
- Produces: `MoldDto.ProductTypeId/ProductTypeArticle/ProductTypeName`; `CreateMoldRequestDto.ProductTypeId (Guid?)`; `UpdateMoldRequestDto.ProductTypeId (Guid?)`; поведение `POST/PUT /api/molds`.

- [ ] **Step 1: Расширить DTO ПФ**

In `MoldDto.cs`, добавить перед `public bool IsActive { get; set; }`:

```csharp
    public Guid? ProductTypeId { get; set; }
    public string? ProductTypeArticle { get; set; }
    public string? ProductTypeName { get; set; }
```

In `CreateMoldRequestDto.cs`, добавить:

```csharp
    public Guid? ProductTypeId { get; set; }
```

In `UpdateMoldRequestDto.cs`, добавить:

```csharp
    public Guid? ProductTypeId { get; set; }
```

- [ ] **Step 2: Написать провальные тесты интеграции ПФ↔тип**

Create `Wintime.Control.Tests.Integration/Molds/MoldProductTypeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldProductTypeTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldProductTypeTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedTypeAsync(bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var pt = new ProductType { Article = $"PT-{Guid.NewGuid():N}", Name = "Изд", IsActive = isActive };
        db.ProductTypes.Add(pt);
        await db.SaveChangesAsync();
        return pt.Id;
    }

    private static CreateMoldRequestDto NewMold(Guid? typeId) => new()
    {
        FormId = $"PF-{Guid.NewGuid():N}", Name = "ПФ", Cavities = 1,
        MaxResourceCycles = 1000, ProductTypeId = typeId
    };

    [Fact]
    public async Task CreateMold_WithoutProductType_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(null));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithArchivedType_Returns400()
    {
        var client = await ManagerClientAsync();
        var archived = await SeedTypeAsync(isActive: false);
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(archived));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithType_BindsAndDtoHasTypeName()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(typeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<MoldDto>();
        dto!.ProductTypeId.Should().Be(typeId);
        dto.ProductTypeName.Should().Be("Изд");
    }

    [Fact]
    public async Task UpdateMold_SetTypeOnLegacyMold_NullToValue_Allowed_EvenWithTasks()
    {
        // Legacy ПФ без типа, но с заданием → присвоение типа разрешено (исключение п.7).
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "Legacy", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = null };
            db.Molds.Add(mold);
            db.ShiftTasks.Add(new Core.Entities.ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = mold.Id, PlanQuantity = 10,
                Status = Core.Enums.TaskStatus.Draft, IssuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = typeId });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateMold_ChangeExistingType_WithTasks_Returns409()
    {
        var client = await ManagerClientAsync();
        var type1 = await SeedTypeAsync();
        var type2 = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "M", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = type1 };
            db.Molds.Add(mold);
            db.ShiftTasks.Add(new Core.Entities.ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = mold.Id, PlanQuantity = 10,
                Status = Core.Enums.TaskStatus.Completed, IssuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = type2 });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateMold_ChangeExistingType_NoTasks_Allowed()
    {
        var client = await ManagerClientAsync();
        var type1 = await SeedTypeAsync();
        var type2 = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "M", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = type1 };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = type2 });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

- [ ] **Step 3: Запустить — убедиться, что падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MoldProductTypeTests"`
Expected: FAIL (валидаций нет, DTO-поля не заполняются).

- [ ] **Step 4: Заполнять поля типа в `MoldDto` (список и by-id)**

In `MoldsController.GetMoldList`, заменить `var query = _context.Molds.AsQueryable();` на:

```csharp
        var query = _context.Molds.Include(m => m.ProductType).AsQueryable();
```

В проекции `molds.Select(m => {...})` добавить в инициализатор `MoldDto` (рядом с `IsActive = m.IsActive`):

```csharp
                ProductTypeId = m.ProductTypeId,
                ProductTypeArticle = m.ProductType?.Article,
                ProductTypeName = m.ProductType?.Name,
```

In `GetMoldById`, заменить `var mold = await _context.Molds.FirstOrDefaultAsync(m => m.Id == id);` на:

```csharp
        var mold = await _context.Molds.Include(m => m.ProductType).FirstOrDefaultAsync(m => m.Id == id);
```

и в его `MoldDto` добавить те же три строки (`ProductTypeId`/`ProductTypeArticle`/`ProductTypeName`).

- [ ] **Step 5: Обязательность типа на create + запрет архивного (UC-4)**

In `MoldsController.CreateMold`, сразу после вычисления `formId` и проверки его уникальности (перед `var mold = new Mold {...}`), добавить:

```csharp
        if (request.ProductTypeId == null)
            return BadRequest("Не указан тип изделия.");
        var productType = await _context.ProductTypes.FindAsync(request.ProductTypeId.Value);
        if (productType == null || !productType.IsActive)
            return BadRequest("Указан несуществующий или архивный тип изделия.");
```

В инициализатор `new Mold { ... }` добавить:

```csharp
            ProductTypeId = request.ProductTypeId,
```

В `MoldDto`, который возвращает `CreateMold` (в конце метода), добавить три поля типа:

```csharp
            ProductTypeId = mold.ProductTypeId,
            ProductTypeArticle = productType.Article,
            ProductTypeName = productType.Name,
```

- [ ] **Step 6: Блокировки смены типа на update (UC-5/UC-6)**

In `MoldsController.UpdateMold`, перед `await _context.SaveChangesAsync();` (после блока `if (request.IsActive.HasValue) ...`), добавить:

```csharp
        if (request.ProductTypeId.HasValue && request.ProductTypeId.Value != mold.ProductTypeId)
        {
            var newType = await _context.ProductTypes.FindAsync(request.ProductTypeId.Value);
            if (newType == null || !newType.IsActive)
                return BadRequest("Указан несуществующий или архивный тип изделия.");

            // п.7: менять УЖЕ заданный тип нельзя, если по ПФ есть задания.
            // Исключение: тип отсутствовал (null → value) — разрешено всегда.
            if (mold.ProductTypeId != null &&
                await _context.ShiftTasks.AnyAsync(t => t.MoldId == id))
            {
                return Conflict("Нельзя изменить тип изделия: по пресс-форме уже есть задания.");
            }

            mold.ProductTypeId = request.ProductTypeId.Value;
        }
```

- [ ] **Step 7: Запустить тесты — убедиться, что проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MoldProductTypeTests"`
Expected: все 6 PASS.

- [ ] **Step 8: Регресс существующих тестов ПФ**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MoldQrTests"`
Expected: все PASS (сид `TestMoldId` уже с типом; MoldQr не создаёт ПФ через API без типа).

- [ ] **Step 9: Commit**

```powershell
git add Wintime.Control.Core/DTOs/Mold/ Wintime.Control.API/Controllers/MoldsController.cs Wintime.Control.Tests.Integration/Molds/MoldProductTypeTests.cs
git commit -m "feat(PZP-09): связь ПФ↔тип — обязательность на create, блокировки смены типа"
```

---

### Task 4: Запрет создания задания для ПФ без типа (UC-7)

**Files:**
- Modify: `Wintime.Control.API/Controllers/TasksController.cs:183-185` (расширить проверку ПФ)
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskProductTypeGuardTests.cs`

**Interfaces:**
- Consumes: `Mold.ProductTypeId` (Task 1); `IntegrationTestFactory.TestImmId/TestProductTypeId` (Task 1).
- Produces: поведение `POST /api/tasks` (400 при ПФ без типа).

- [ ] **Step 1: Написать провальный тест**

Create `Wintime.Control.Tests.Integration/Tasks/TaskProductTypeGuardTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskProductTypeGuardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskProductTypeGuardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task CreateTask_ForMoldWithoutType_Returns400()
    {
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "NoType", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = null };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, planQuantity = 10, note = "x"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_ForMoldWithType_Returns201()
    {
        // TestMoldId сидируется с TestProductTypeId → задание создаётся.
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId = _factory.TestMoldId, planQuantity = 10, note = "x"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что первый падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskProductTypeGuardTests"`
Expected: `CreateTask_ForMoldWithoutType_Returns400` FAIL (создаётся 201 вместо 400); второй PASS.

- [ ] **Step 3: Добавить guard в `CreateTask`**

In `TasksController.cs`, в методе `CreateTask`, заменить блок:

```csharp
        var mold = await _context.Molds.FindAsync(request.MoldId);
        if (mold == null || !mold.IsActive)
            return BadRequest("Указана неактивная или несуществующая пресс-форма.");
```

на:

```csharp
        var mold = await _context.Molds.FindAsync(request.MoldId);
        if (mold == null || !mold.IsActive)
            return BadRequest("Указана неактивная или несуществующая пресс-форма.");
        if (mold.ProductTypeId == null)
            return BadRequest("У пресс-формы не задан тип изделия.");
```

- [ ] **Step 4: Запустить тесты — убедиться, что проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskProductTypeGuardTests"`
Expected: оба PASS.

- [ ] **Step 5: Полный регресс backend**

Run: `dotnet test`
Expected: все тесты (unit + integration) зелёные. Особое внимание — `TasksControllerTests` (используют `TestMoldId`, теперь с типом).

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/TaskProductTypeGuardTests.cs
git commit -m "feat(PZP-09): запрет создания задания для ПФ без типа изделия (UC-7)"
```

---

### Task 5: Фронтенд — справочник изделий (`ProductTypeDictionary.vue` + api + роутинг)

**Files:**
- Create: `Wintime-Control-Frontend/src/api/productTypes.js`
- Create: `Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/router/index.js` (роут в группе `dictionary`)
- Modify: навигационное меню (пункт «Изделия») — файл найти по существующим пунктам справочников (см. Step 4)

**Interfaces:**
- Consumes: `GET/POST/PUT /api/producttypes` (Task 2).
- Produces: `productTypesApi { getList, getById, create, update }`; роут `DictionaryProductTypes`.

- [ ] **Step 1: API-модуль**

Create `Wintime-Control-Frontend/src/api/productTypes.js`:

```javascript
import apiClient from './client'

export const productTypesApi = {
  getList(params) {
    return apiClient.get('/producttypes', { params })
  },
  getById(id) {
    return apiClient.get(`/producttypes/${id}`)
  },
  create(data) {
    return apiClient.post('/producttypes', data)
  },
  update(id, data) {
    return apiClient.put(`/producttypes/${id}`, data)
  }
}
```

- [ ] **Step 2: Справочник изделий**

Create `Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue`:

```vue
<template>
  <div>
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Справочник изделий</h2>
        <p class="text-gray-600 mt-1">Номенклатура выпускаемых изделий</p>
      </div>
      <el-button type="primary" @click="showCreateModal">
        <el-icon class="mr-1"><Plus /></el-icon>
        Добавить изделие
      </el-button>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true" :model="filters">
        <el-form-item label="Поиск">
          <el-input v-model="filters.search" placeholder="Артикул или наименование" clearable />
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

    <el-table :data="items" stripe style="width: 100%" v-loading="loading">
      <el-table-column prop="article" label="Артикул" width="200" />
      <el-table-column prop="name" label="Наименование" />
      <el-table-column label="Статус" width="120" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? 'Активно' : 'В архиве' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="Действия" width="220" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="editItem(row)">Редактировать</el-button>
          <el-button size="small" :type="row.isActive ? 'danger' : 'success'" @click="toggleArchive(row)">
            {{ row.isActive ? 'В архив' : 'Вернуть' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="dialogVisible"
      :title="editing ? 'Редактирование изделия' : 'Новое изделие'"
      width="500px"
    >
      <el-form :model="form" label-width="140px" :rules="rules" ref="formRef">
        <el-form-item label="Артикул" prop="article" required>
          <el-input v-model="form.article" placeholder="ART-001" />
        </el-form-item>
        <el-form-item label="Наименование" prop="name" required>
          <el-input v-model="form.name" placeholder="Крышка 48мм" />
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
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { productTypesApi } from '@/api/productTypes'

const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref(null)
const formRef = ref(null)
const items = ref([])

const filters = reactive({ search: '', isActive: null })
const form = reactive({ article: '', name: '' })

const rules = {
  article: [{ required: true, message: 'Введите артикул', trigger: 'blur' }],
  name: [{ required: true, message: 'Введите наименование', trigger: 'blur' }]
}

onMounted(loadItems)

async function loadItems() {
  loading.value = true
  try {
    const { data } = await productTypesApi.getList({
      isActive: filters.isActive,
      search: filters.search
    })
    items.value = data
  } catch {
    ElMessage.error('Ошибка загрузки изделий')
  } finally {
    loading.value = false
  }
}

function showCreateModal() {
  editing.value = null
  Object.assign(form, { article: '', name: '' })
  dialogVisible.value = true
}

function editItem(row) {
  editing.value = row
  Object.assign(form, { article: row.article, name: row.name })
  dialogVisible.value = true
}

async function save() {
  if (!formRef.value) return
  await formRef.value.validate(async (valid) => {
    if (!valid) return
    saving.value = true
    try {
      if (editing.value) {
        await productTypesApi.update(editing.value.id, form)
        ElMessage.success('Изделие обновлено')
      } else {
        await productTypesApi.create(form)
        ElMessage.success('Изделие создано')
      }
      dialogVisible.value = false
      await loadItems()
    } catch (error) {
      ElMessage.error(error.response?.data ?? 'Ошибка сохранения изделия')
    } finally {
      saving.value = false
    }
  })
}

async function toggleArchive(row) {
  try {
    await productTypesApi.update(row.id, { isActive: !row.isActive })
    ElMessage.success(row.isActive ? 'Изделие в архиве' : 'Изделие возвращено')
    await loadItems()
  } catch {
    ElMessage.error('Ошибка изменения статуса')
  }
}
</script>
```

- [ ] **Step 3: Роут в группе `dictionary`**

In `src/router/index.js`, в массив `children` группы `dictionary` (рядом с `DictionaryMolds`, строки ~78-83) добавить:

```javascript
          {
            path: 'product-types',
            name: 'DictionaryProductTypes',
            component: () => import('@/views/dictionary/ProductTypeDictionary.vue'),
          },
```

- [ ] **Step 4: Пункт меню «Изделия»**

Найти навигационное меню справочников:
Run: `grep -rn "DictionaryMolds\|Пресс-формы" Wintime-Control-Frontend/src --include=*.vue`
Открыть файл(ы), где рендерится пункт «Пресс-формы» (`el-menu-item`/route link), и добавить рядом пункт «Изделия», указывающий на роут `DictionaryProductTypes` (`path: '/dictionary/product-types'` — сверься с тем, как построены другие пункты: по `name` роута или по `index`-пути). Скопировать разметку соседнего пункта, заменив подпись на «Изделия» и цель на новый роут.

- [ ] **Step 5: Manual smoke**

Run (два терминала):
```powershell
dotnet run --project Wintime.Control.API
```
```powershell
cd Wintime-Control-Frontend; npm run dev
```
В браузере (`http://localhost:3000`), под менеджером:
1. Открыть справочник «Изделия» → создать изделие (артикул + наименование) → появилось в списке.
2. Создать второе с тем же артикулом → ошибка «уже используется».
3. Редактировать наименование → сохранилось.
4. «В архив» → тег «В архиве»; фильтр «Активные» его скрывает; «Вернуть» возвращает.

Ожидаемо: все шаги работают, ошибок в консоли нет.

- [ ] **Step 6: Commit**

```powershell
git add Wintime-Control-Frontend/src/api/productTypes.js Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue Wintime-Control-Frontend/src/router/index.js
git add -A Wintime-Control-Frontend/src   # файл(ы) меню
git commit -m "feat(PZP-09): фронт — справочник изделий (ProductTypeDictionary) + роут + меню"
```

---

### Task 6: Фронтенд — тип изделия в справочнике ПФ (колонка + обязательный селект)

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue` (колонка, селект, правило, form-модель)

**Interfaces:**
- Consumes: `productTypesApi.getList` (Task 5); `MoldDto.productTypeId/productTypeName` (Task 3).
- Produces: обязательный выбор типа при создании/редактировании ПФ.

- [ ] **Step 1: Импорт api и загрузка активных типов**

In `MoldDictionary.vue` `<script setup>`, после `import { moldsApi } from '@/api/molds'` добавить:

```javascript
import { productTypesApi } from '@/api/productTypes'
```

Рядом с `const molds = ref([])` добавить:

```javascript
const productTypes = ref([])
```

В `onMounted`, рядом с `await loadMolds()`, добавить загрузку активных типов:

```javascript
  const { data } = await productTypesApi.getList({ isActive: true })
  productTypes.value = data
```

- [ ] **Step 2: Поле в form-модели и сбросе**

В объект `const form = reactive({ ... })` добавить `productTypeId: null,`. В `showCreateModal` (объект `Object.assign(form, {...})`) добавить `productTypeId: null,`. В `editMold` (`Object.assign(form, {...})`) добавить `productTypeId: mold.productTypeId ?? null,`.

- [ ] **Step 3: Правило обязательности**

В объект `const rules = { ... }` добавить:

```javascript
  productTypeId: [{ required: true, message: 'Выберите тип изделия', trigger: 'change' }],
```

- [ ] **Step 4: Селект в форме**

В `<el-form>` модалки, сразу после блока `el-form-item` с наименованием (после закрывающего `</el-col>` строки с `name`), добавить строку с селектом:

```vue
        <el-row :gutter="20">
          <el-col :span="24">
            <el-form-item label="Тип изделия" prop="productTypeId" required>
              <el-select
                v-model="form.productTypeId"
                filterable
                placeholder="Выберите изделие"
                class="w-full"
              >
                <el-option
                  v-for="pt in productTypes"
                  :key="pt.id"
                  :label="`${pt.article} · ${pt.name}`"
                  :value="pt.id"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
```

> Примечание по редактированию legacy-ПФ: если у ПФ был тип, а список активных типов его не содержит (архивный) — селект покажет пусто. Для этого спринта достаточно; полноценный показ архивного текущего значения — вне объёма.

- [ ] **Step 5: Колонка «Тип изделия» в таблице**

В `<el-table>`, после `<el-table-column prop="name" label="Наименование" />` добавить:

```vue
      <el-table-column label="Тип изделия" width="200">
        <template #default="{ row }">
          <span v-if="row.productTypeName">{{ row.productTypeArticle }} · {{ row.productTypeName }}</span>
          <el-tag v-else type="warning" size="small">не задан</el-tag>
        </template>
      </el-table-column>
```

- [ ] **Step 6: Показ серверной ошибки при сохранении**

В `saveMold`, в `catch (error)` заменить `ElMessage.error('Ошибка сохранения пресс-формы')` на:

```javascript
      ElMessage.error(error.response?.data ?? 'Ошибка сохранения пресс-формы')
```

(чтобы 400/409 «архивный тип»/«нельзя изменить тип» отображались пользователю).

- [ ] **Step 7: Manual smoke**

При запущенных API + фронте, под менеджером:
1. Справочник ПФ → «Добавить пресс-форму»: попытка сохранить без типа → валидатор «Выберите тип изделия».
2. Выбрать тип, сохранить → ПФ создана, в колонке «Тип изделия» — артикул · наименование.
3. Создать задание для этой ПФ (раздел заданий) → создаётся.
4. Отредактировать ПФ, у которой есть задание, попытаться сменить тип → серверная ошибка «Нельзя изменить тип изделия…».
5. Legacy-ПФ без типа (сид/старая) в списке показывает тег «не задан».

Ожидаемо: все шаги работают.

- [ ] **Step 8: Commit**

```powershell
git add Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue
git commit -m "feat(PZP-09): фронт — обязательный тип изделия в справочнике ПФ + колонка"
```

---

## Финализация

- [ ] **Полный прогон тестов backend:** `dotnet test` — всё зелёное.
- [ ] **Обновить CLAUDE.md** (секция домена): добавить короткое правило — «Тип изделия (`ProductType`) обязателен для новых ПФ и для создания задания; у ПФ с заданиями заданный тип не меняется (искл. `null → value`). См. ADR-0007.» Commit отдельным `docs:`-коммитом.
- [ ] **Финиш ветки:** использовать skill `superpowers:finishing-a-development-branch` (PR в master — master защищён, прямой push запрещён).

## Self-Review (выполнено при написании плана)

- **Покрытие спеки:** UC-1/UC-2 (Task 2), UC-3 (Task 2 `Archive_...`), UC-4 (Task 3 create-тесты), UC-5 (Task 3 `NullToValue`), UC-6 (Task 3 `ChangeExistingType_WithTasks`), UC-7 (Task 4), UC-8 (Task 3 `...DtoHasTypeName` + фронт Task 6). Уникальность (Task 2). Миграция/схема (Task 1). Фронт-справочник (Task 5), селект в ПФ (Task 6).
- **Типы согласованы:** `ProductTypeId`/`ProductTypeArticle`/`ProductTypeName` — одинаково в DTO (Task 3) и фронте (Task 6, camelCase `productTypeId`/`productTypeArticle`/`productTypeName` — сериализация Web JSON). `productTypesApi` — один и тот же в Task 5/6.
- **Плейсхолдеров нет;** единственный «найди файл меню» (Task 5 Step 4) снабжён точной grep-командой — структура меню не была прочитана, поэтому даю способ найти, а не выдуманный путь.
- **Известный нюанс тестов PUT:** контроллер возвращает `204 NoContent` (не `200 OK`, как `DowntimeReasonController`) — ожидания в тестах Task 2 приведены к `NoContent` (Step 5 явно указывает исправить).
