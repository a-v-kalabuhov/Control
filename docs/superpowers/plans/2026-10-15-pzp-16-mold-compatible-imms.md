# PZP-16 — Совместимые ТПА в карточке ПФ: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** У пресс-формы — ручной список совместимых ТПА; задание с несовместимой парой ПФ↔ТПА создать нельзя, форма задания фильтрует выбор.

**Architecture:** Связь many-to-many `Mold ↔ Imm` через таблицу `MoldCompatibleImms` (EF skip navigation, только со стороны `Mold`). Правило «пусто = любой ТПА» — чистая функция `MoldImmCompatibility.IsCompatible` в `Core/Policies`, её вызывает `TasksController.CreateTask`. `MoldsController` отдаёт список в `MoldDto` и принимает `CompatibleImmIds` (полная замена). На фронте — мультиселект в `MoldDictionary.vue` и вычисляемые фильтры в `TaskFormModal.vue` через утилиту `moldCompatibility.js`.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions + Testcontainers; Vue 3, Element Plus, Vitest + @vue/test-utils.

**Спека:** `docs/superpowers/specs/2026-10-15-pzp-16-mold-compatible-imms-design.md`

## Global Constraints

- Пустой список совместимых ТПА = «совместимость не указана» = ПФ подходит к **любому** ТПА.
- Несовместимую пару задания создать нельзя: API → 400 «Пресс-форма не совместима с выбранным ТПА.»
- Архивный ТПА (`IsActive=false`) добавить в список нельзя (400 «Нельзя добавить архивный ТПА в список совместимых.»); если он уже был в списке ПФ до запроса — связь сохраняется.
- Несуществующий ТПА в списке → 400 «Указан несуществующий ТПА.»
- `UpdateMoldRequestDto.CompatibleImmIds`: `null` → список не трогать; массив → полная замена (`[]` — очистка). Дубликаты схлопываются.
- Физически ничего не удаляем; права не меняются (читают Admin/Manager/Adjuster, пишут Admin/Manager).
- Обратного вида в карточке ТПА нет. Существующие задания не помечаются и не блокируются.
- В режиме редактирования задания фильтр совместимости в форме **не** применяется.
- Тесты: xUnit для .NET, Vitest для фронта. Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Файловая структура

| Файл | Действие | Ответственность |
|---|---|---|
| `Wintime.Control.Core/Policies/MoldImmCompatibility.cs` | Create | Правило совместимости |
| `Wintime.Control.Tests.Unit/Policies/MoldImmCompatibilityTests.cs` | Create | Unit-тесты правила |
| `Wintime.Control.Core/Entities/Mold.cs` | Modify | Навигация `CompatibleImms` |
| `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` | Modify | Конфигурация связи |
| `Wintime.Control.Infrastructure/Migrations/*_AddMoldCompatibleImms.cs` | Create (генерируется) | Таблица связи |
| `Wintime.Control.Core/DTOs/Mold/MoldCompatibleImmDto.cs` | Create | Элемент списка в DTO |
| `Wintime.Control.Core/DTOs/Mold/MoldDto.cs`, `CreateMoldRequestDto.cs`, `UpdateMoldRequestDto.cs` | Modify | Новые поля |
| `Wintime.Control.API/Controllers/MoldsController.cs` | Modify | Чтение/запись/валидация списка, общий `ToDto` |
| `Wintime.Control.Tests.Integration/Molds/MoldCompatibleImmsTests.cs` | Create | API ПФ |
| `Wintime.Control.API/Controllers/TasksController.cs` | Modify | Проверка в `CreateTask` |
| `Wintime.Control.Tests.Integration/Tasks/TaskMoldCompatibilityGuardTests.cs` | Create | API задания |
| `Wintime-Control-Frontend/src/utils/moldCompatibility.js` | Create | `isMoldCompatibleWithImm`, `formatCompatibleImms`, `immOptionLabel` |
| `Wintime-Control-Frontend/src/utils/__tests__/moldCompatibility.spec.js` | Create | Тесты утилиты |
| `Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue` | Modify | Поле и колонка |
| `Wintime-Control-Frontend/src/views/dictionary/__tests__/MoldDictionary.spec.js` | Create | Тесты справочника |
| `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue` | Modify | Фильтры ПФ/ТПА |
| `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js` | Modify | Тесты фильтров |
| `docs/adr/0018-mold-imm-compatibility-manual-list.md`, `docs/adr/README.md`, `CLAUDE.md`, `docs/domain/mold.md`, `docs/domain/README.md`, `docs/user-guide/manager.md` | Create/Modify | Документация |

---

### Task 1: Доменное правило `MoldImmCompatibility`

**Files:**
- Create: `Wintime.Control.Core/Policies/MoldImmCompatibility.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/MoldImmCompatibilityTests.cs`

**Interfaces:**
- Produces: `public static bool MoldImmCompatibility.IsCompatible(IReadOnlyCollection<Guid> compatibleImmIds, Guid immId)` (namespace `Wintime.Control.Core.Policies`).

- [ ] **Step 1: Написать падающий тест**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class MoldImmCompatibilityTests
{
    [Fact]
    public void IsCompatible_EmptyList_AnyImmIsCompatible()
    {
        MoldImmCompatibility.IsCompatible(Array.Empty<Guid>(), Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void IsCompatible_ImmInList_True()
    {
        var imm = Guid.NewGuid();
        MoldImmCompatibility.IsCompatible(new[] { Guid.NewGuid(), imm }, imm).Should().BeTrue();
    }

    [Fact]
    public void IsCompatible_ImmNotInList_False()
    {
        MoldImmCompatibility.IsCompatible(new[] { Guid.NewGuid() }, Guid.NewGuid()).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Убедиться, что тест падает**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~MoldImmCompatibilityTests"`
Expected: ошибка компиляции — `MoldImmCompatibility` не существует.

- [ ] **Step 3: Реализация**

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Совместимость пресс-формы с ТПА (PZP-16, ADR-0018): ручной список совместимых ТПА в карточке ПФ.
/// Пустой список — совместимость не указана, ПФ подходит к любому ТПА.
/// Чистая функция — вызывающий код сам грузит ID совместимых ТПА.
/// </summary>
public static class MoldImmCompatibility
{
    public static bool IsCompatible(IReadOnlyCollection<Guid> compatibleImmIds, Guid immId)
        => compatibleImmIds.Count == 0 || compatibleImmIds.Contains(immId);
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~MoldImmCompatibilityTests"`
Expected: PASS, 3 теста.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/MoldImmCompatibility.cs Wintime.Control.Tests.Unit/Policies/MoldImmCompatibilityTests.cs
git commit -m "feat(core): add MoldImmCompatibility policy (PZP-16)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Схема и API пресс-формы — список совместимых ТПА

**Files:**
- Modify: `Wintime.Control.Core/Entities/Mold.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` (блок `// Конфигурация Mold`)
- Create (генерируется): `Wintime.Control.Infrastructure/Migrations/<timestamp>_AddMoldCompatibleImms.cs` + `.Designer.cs`, правка `ControlDbContextModelSnapshot.cs`
- Create: `Wintime.Control.Core/DTOs/Mold/MoldCompatibleImmDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Mold/MoldDto.cs`, `CreateMoldRequestDto.cs`, `UpdateMoldRequestDto.cs`
- Modify: `Wintime.Control.API/Controllers/MoldsController.cs`
- Test: `Wintime.Control.Tests.Integration/Molds/MoldCompatibleImmsTests.cs`

**Interfaces:**
- Produces: `Mold.CompatibleImms : ICollection<Imm>`; таблица `MoldCompatibleImms (MoldId, ImmId)`.
- Produces (JSON): `MoldDto.compatibleImms: [{ id, name, isActive }]`, сортировка по `name`; запросы `compatibleImmIds: Guid[] | null`.

- [ ] **Step 1: Написать падающие интеграционные тесты**

Integration-тесты поднимают Postgres через Testcontainers и применяют миграции при старте API (`Program.cs` → `db.Database.Migrate()`), поэтому нужен запущенный Docker.

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldCompatibleImmsTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldCompatibleImmsTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedImmAsync(string name, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Imm { Name = name, TemplateId = _factory.TestTemplateId, IsActive = isActive };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    /// Создаёт ПФ напрямую в БД с заданными совместимыми ТПА (в обход API — чтобы
    /// заранее привязать уже архивный ТПА).
    private async Task<Guid> SeedMoldAsync(params Guid[] compatibleImmIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imms = await db.Imms.Where(i => compatibleImmIds.Contains(i.Id)).ToListAsync();
        var mold = new Mold
        {
            Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 1, MaxResourceCycles = 1000,
            IsActive = true, ProductTypeId = _factory.TestProductTypeId, CompatibleImms = imms
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private CreateMoldRequestDto NewMold(params Guid[] compatibleImmIds) => new()
    {
        FormId = $"PF-{Guid.NewGuid():N}", Name = "ПФ", Cavities = 1, MaxResourceCycles = 1000,
        ProductTypeId = _factory.TestProductTypeId, CompatibleImmIds = compatibleImmIds.ToList()
    };

    private static async Task<MoldDto> GetMoldAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<MoldDto>($"/api/molds/{id}"))!;

    [Fact]
    public async Task CreateMold_WithCompatibleImms_ReturnsSortedListInDto()
    {
        var client = await ManagerClientAsync();
        var b = await SeedImmAsync("ТПА-B");
        var a = await SeedImmAsync("ТПА-A");

        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(b, a, a));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<MoldDto>();
        dto!.CompatibleImms.Select(i => i.Name).Should().Equal("ТПА-A", "ТПА-B");
        dto.CompatibleImms.Should().OnlyContain(i => i.IsActive);
    }

    [Fact]
    public async Task CreateMold_WithoutCompatibleImms_HasEmptyList()
    {
        var client = await ManagerClientAsync();
        var request = NewMold();
        request.CompatibleImmIds = null;

        var resp = await client.PostAsJsonAsync("/api/molds", request);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        (await resp.Content.ReadFromJsonAsync<MoldDto>())!.CompatibleImms.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMold_WithUnknownImm_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(Guid.NewGuid()));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithArchivedImm_Returns400()
    {
        var client = await ManagerClientAsync();
        var archived = await SeedImmAsync("ТПА-архив", isActive: false);
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(archived));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMold_NullList_KeepsCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var imm = await SeedImmAsync("ТПА-1");
        var moldId = await SeedMoldAsync(imm);

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new UpdateMoldRequestDto { Name = "Новое" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetMoldAsync(client, moldId)).CompatibleImms.Select(i => i.Id).Should().Equal(imm);
    }

    [Fact]
    public async Task UpdateMold_EmptyList_ClearsCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var moldId = await SeedMoldAsync(await SeedImmAsync("ТПА-1"));

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid>() });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetMoldAsync(client, moldId)).CompatibleImms.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateMold_List_ReplacesCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var oldImm = await SeedImmAsync("ТПА-old");
        var newImm = await SeedImmAsync("ТПА-new");
        var moldId = await SeedMoldAsync(oldImm);

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid> { newImm } });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetMoldAsync(client, moldId)).CompatibleImms.Select(i => i.Id).Should().Equal(newImm);
    }

    [Fact]
    public async Task UpdateMold_AddArchivedImm_Returns400()
    {
        var client = await ManagerClientAsync();
        var moldId = await SeedMoldAsync();
        var archived = await SeedImmAsync("ТПА-архив", isActive: false);

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid> { archived } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMold_KeepPreviouslyLinkedArchivedImm_Allowed()
    {
        var client = await ManagerClientAsync();
        var archivedLater = await SeedImmAsync("ТПА-уйдёт-в-архив");
        var active = await SeedImmAsync("ТПА-активный");
        var moldId = await SeedMoldAsync(archivedLater);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = await db.Imms.FindAsync(archivedLater);
            imm!.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid> { archivedLater, active } });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dto = await GetMoldAsync(client, moldId);
        dto.CompatibleImms.Should().HaveCount(2);
        dto.CompatibleImms.Single(i => i.Id == archivedLater).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetMoldList_ContainsCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var imm = await SeedImmAsync("ТПА-список");
        var moldId = await SeedMoldAsync(imm);

        var list = await client.GetFromJsonAsync<List<MoldDto>>("/api/molds");
        list!.Single(m => m.Id == moldId).CompatibleImms.Select(i => i.Id).Should().Equal(imm);
    }
}
```

- [ ] **Step 2: Убедиться, что тест падает**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MoldCompatibleImmsTests"`
Expected: ошибка компиляции — нет `Mold.CompatibleImms`, `MoldDto.CompatibleImms`, `CompatibleImmIds`.

- [ ] **Step 3: Сущность и конфигурация EF**

В `Wintime.Control.Core/Entities/Mold.cs` перед блоком `// Calculated (not mapped directly, calculated via Usage)` добавить:

```csharp
    // Совместимые ТПА (PZP-16, ADR-0018). Пусто — совместимость не указана, ПФ подходит к любому ТПА.
    // Архивный ТПА добавить нельзя, но ранее привязанный остаётся в списке.
    public ICollection<Imm> CompatibleImms { get; set; } = new List<Imm>();
```

В `ControlDbContext.OnModelCreating`, в блоке `builder.Entity<Mold>(entity => { ... })` после конфигурации `ProductType` добавить:

```csharp
            // Совместимые ТПА (PZP-16): many-to-many без класса связи, навигация только у Mold.
            entity.HasMany(e => e.CompatibleImms)
                  .WithMany()
                  .UsingEntity<Dictionary<string, object>>(
                      "MoldCompatibleImms",
                      r => r.HasOne<Imm>().WithMany().HasForeignKey("ImmId").OnDelete(DeleteBehavior.Restrict),
                      l => l.HasOne<Mold>().WithMany().HasForeignKey("MoldId").OnDelete(DeleteBehavior.Cascade),
                      j =>
                      {
                          j.HasKey("MoldId", "ImmId");
                          j.HasIndex("ImmId");
                      });
```

- [ ] **Step 4: Сгенерировать миграцию**

Run (из корня репо):
```powershell
dotnet ef migrations add AddMoldCompatibleImms --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```
Expected: созданы `*_AddMoldCompatibleImms.cs`/`.Designer.cs`. Открыть `.cs` и проверить: `Up` создаёт только таблицу `MoldCompatibleImms` с PK `(MoldId, ImmId)`, FK на `Molds` (Cascade) и `Imms` (Restrict), индекс `IX_MoldCompatibleImms_ImmId`; никаких других изменений схемы. Если появились лишние операции — остановиться и разобраться (снапшот рассинхронизирован).

- [ ] **Step 5: DTO**

`Wintime.Control.Core/DTOs/Mold/MoldCompatibleImmDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Mold;

/// <summary>ТПА из списка совместимых с пресс-формой (PZP-16).</summary>
public class MoldCompatibleImmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
```

В `MoldDto` после `ProductTypeName`:

```csharp
    // Совместимые ТПА (PZP-16), по имени. Пусто — ПФ подходит к любому ТПА.
    public List<MoldCompatibleImmDto> CompatibleImms { get; set; } = new();
```

В `CreateMoldRequestDto` после `ProductTypeId`:

```csharp
    // Совместимые ТПА (PZP-16). null/пусто — совместимость не указана.
    public List<Guid>? CompatibleImmIds { get; set; }
```

В `UpdateMoldRequestDto` после `ProductTypeId`:

```csharp
    // Совместимые ТПА (PZP-16). null — не менять; массив — полная замена ([] — очистка).
    public List<Guid>? CompatibleImmIds { get; set; }
```

- [ ] **Step 6: Контроллер**

В `MoldsController.cs`:

1. Добавить в конец класса два приватных хелпера:

```csharp
    private static MoldDto ToDto(Mold m, int totalCycles) => new()
    {
        Id = m.Id,
        FormId = m.FormId,
        Name = m.Name,
        Cavities = m.Cavities,
        PartWeightGrams = m.PartWeightGrams,
        RunnerWeightGrams = m.RunnerWeightGrams,
        MaxResourceCycles = m.MaxResourceCycles,
        To1Cycles = m.To1Cycles,
        To2Cycles = m.To2Cycles,
        StorageLocationIndex = m.StorageLocationIndex,
        DrawingPath = m.DrawingPath,
        PhotoPath = m.PhotoPath,
        TotalCycles = totalCycles,
        RemainingResource = m.MaxResourceCycles - totalCycles,
        ProductTypeId = m.ProductTypeId,
        ProductTypeArticle = m.ProductType?.Article,
        ProductTypeName = m.ProductType?.Name,
        CompatibleImms = m.CompatibleImms
            .OrderBy(i => i.Name)
            .Select(i => new MoldCompatibleImmDto { Id = i.Id, Name = i.Name, IsActive = i.IsActive })
            .ToList(),
        IsActive = m.IsActive
    };

    /// <summary>
    /// Проверяет запрошенный список совместимых ТПА (PZP-16): все ID существуют; архивный ТПА
    /// допустим, только если уже был в списке ПФ (как архивный тип изделия в ADR-0007).
    /// Возвращает ТПА для присвоения либо текст ошибки 400.
    /// </summary>
    private async Task<(List<Imm>? Imms, string? Error)> ResolveCompatibleImmsAsync(
        IEnumerable<Guid> requestedIds, IEnumerable<Imm> currentImms)
    {
        var ids = requestedIds.Distinct().ToList();
        var imms = await _context.Imms.Where(i => ids.Contains(i.Id)).ToListAsync();
        if (imms.Count != ids.Count)
            return (null, "Указан несуществующий ТПА.");

        var currentIds = currentImms.Select(i => i.Id).ToHashSet();
        if (imms.Any(i => !i.IsActive && !currentIds.Contains(i.Id)))
            return (null, "Нельзя добавить архивный ТПА в список совместимых.");

        return (imms, null);
    }
```

2. `GetMoldList`: запрос — `_context.Molds.Include(m => m.ProductType).Include(m => m.CompatibleImms).AsQueryable();`, а построение DTO заменить на:

```csharp
        var dtos = molds.Select(m => ToDto(m, cycleMap.GetValueOrDefault(m.Id))).ToList();
```

3. `GetMoldById`: загрузка — `_context.Molds.Include(m => m.ProductType).Include(m => m.CompatibleImms).FirstOrDefaultAsync(m => m.Id == id);`, построение DTO заменить на `return Ok(ToDto(mold, totalCycles));` (переменная `totalCycles` остаётся).

4. `CreateMold`: после проверки типа изделия добавить

```csharp
        var (compatibleImms, immError) = await ResolveCompatibleImmsAsync(
            request.CompatibleImmIds ?? new List<Guid>(), Array.Empty<Imm>());
        if (immError != null)
            return BadRequest(immError);
```

в инициализатор `new Mold { ... }` добавить `CompatibleImms = compatibleImms!,`, а блок `var dto = new MoldDto { ... };` заменить на

```csharp
        var dto = ToDto(mold, 0);
```

(`mold.ProductType` заполнен фикс-апом EF: `productType` уже отслеживается контекстом после `FindAsync`.)

5. `UpdateMold`: загрузку заменить на

```csharp
        var mold = await _context.Molds.Include(m => m.CompatibleImms).FirstOrDefaultAsync(m => m.Id == id);
```

и перед `await _context.SaveChangesAsync();` добавить

```csharp
        if (request.CompatibleImmIds != null)
        {
            var (compatibleImms, immError) = await ResolveCompatibleImmsAsync(
                request.CompatibleImmIds, mold.CompatibleImms);
            if (immError != null)
                return BadRequest(immError);

            mold.CompatibleImms.Clear();
            foreach (var imm in compatibleImms!)
                mold.CompatibleImms.Add(imm);
        }
```

- [ ] **Step 7: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Molds"`
Expected: PASS — новые 10 тестов и прежние `MoldProductTypeTests`/`MoldQrTests`.

- [ ] **Step 8: Commit**

```bash
git add Wintime.Control.Core Wintime.Control.Infrastructure Wintime.Control.API/Controllers/MoldsController.cs Wintime.Control.Tests.Integration/Molds/MoldCompatibleImmsTests.cs
git commit -m "feat(molds): compatible IMMs list on mold (PZP-16)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Запрет несовместимой пары в `CreateTask`

**Files:**
- Modify: `Wintime.Control.API/Controllers/TasksController.cs` (метод `CreateTask`, сразу после проверки `mold.ProductTypeId == null`)
- Test: `Wintime.Control.Tests.Integration/Tasks/TaskMoldCompatibilityGuardTests.cs`

**Interfaces:**
- Consumes: `MoldImmCompatibility.IsCompatible` (Task 1), `Mold.CompatibleImms` (Task 2).

- [ ] **Step 1: Написать падающие тесты**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskMoldCompatibilityGuardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskMoldCompatibilityGuardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    /// ПФ с типом изделия; compatibleImmIds — её список совместимых ТПА.
    private async Task<Guid> SeedMoldAsync(params Guid[] compatibleImmIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imms = await db.Imms.Where(i => compatibleImmIds.Contains(i.Id)).ToListAsync();
        var mold = new Mold
        {
            Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 1, MaxResourceCycles = 1000,
            IsActive = true, ProductTypeId = _factory.TestProductTypeId, CompatibleImms = imms
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private async Task<Guid> SeedOtherImmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Imm { Name = $"ТПА-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private async Task<HttpResponseMessage> CreateTaskAsync(Guid moldId)
    {
        var client = await ManagerClientAsync();
        return await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, planQuantity = 10, note = "x",
            plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            plannedFullCycleSeconds = 30
        });
    }

    [Fact]
    public async Task CreateTask_MoldWithEmptyList_Returns201()
    {
        var resp = await CreateTaskAsync(await SeedMoldAsync());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateTask_ImmInMoldList_Returns201()
    {
        var resp = await CreateTaskAsync(await SeedMoldAsync(_factory.TestImmId));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateTask_ImmNotInMoldList_Returns400()
    {
        var moldId = await SeedMoldAsync(await SeedOtherImmAsync());

        var resp = await CreateTaskAsync(moldId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("не совместима");
    }
}
```

- [ ] **Step 2: Убедиться, что падает только запрет**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~TaskMoldCompatibilityGuardTests"`
Expected: `CreateTask_ImmNotInMoldList_Returns400` FAIL (приходит 201), два других PASS.

- [ ] **Step 3: Реализация**

В `TasksController.CreateTask` сразу после

```csharp
        if (mold.ProductTypeId == null)
            return BadRequest("У пресс-формы не задан тип изделия.");
```

добавить

```csharp
        // PZP-16: ТПА должен входить в список совместимых ПФ (пустой список — любой ТПА).
        var compatibleImmIds = await _context.Molds
            .Where(m => m.Id == request.MoldId)
            .SelectMany(m => m.CompatibleImms.Select(i => i.Id))
            .ToListAsync();
        if (!Wintime.Control.Core.Policies.MoldImmCompatibility.IsCompatible(compatibleImmIds, request.ImmId))
            return BadRequest("Пресс-форма не совместима с выбранным ТПА.");
```

(Полное имя — как у соседнего `Wintime.Control.Core.Policies.OrderTaskBinding` в этом же файле.)

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Tasks"`
Expected: PASS — новые 3 и все прежние тесты заданий.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.API/Controllers/TasksController.cs Wintime.Control.Tests.Integration/Tasks/TaskMoldCompatibilityGuardTests.cs
git commit -m "feat(tasks): reject task for mold incompatible with IMM (PZP-16)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Фронт — утилита и поле «Совместимые ТПА» в справочнике ПФ

**Files:**
- Create: `Wintime-Control-Frontend/src/utils/moldCompatibility.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/moldCompatibility.spec.js`
- Modify: `Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue`
- Test: `Wintime-Control-Frontend/src/views/dictionary/__tests__/MoldDictionary.spec.js`

**Interfaces:**
- Consumes (JSON из Task 2): `mold.compatibleImms: [{ id, name, isActive }]`; запросы `compatibleImmIds: string[]`.
- Produces: `isMoldCompatibleWithImm(mold, immId) → boolean`, `immOptionLabel(imm) → string`, `formatCompatibleImms(list) → string` (`''` для пустого списка).

Все команды фронта — из каталога `Wintime-Control-Frontend`.

- [ ] **Step 1: Падающие тесты утилиты**

`src/utils/__tests__/moldCompatibility.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import { isMoldCompatibleWithImm, immOptionLabel, formatCompatibleImms } from '../moldCompatibility'

describe('isMoldCompatibleWithImm', () => {
  it('пустой список — ПФ подходит к любому ТПА', () => {
    expect(isMoldCompatibleWithImm({ compatibleImms: [] }, 'imm-1')).toBe(true)
  })
  it('без поля compatibleImms — как пустой список', () => {
    expect(isMoldCompatibleWithImm({}, 'imm-1')).toBe(true)
  })
  it('ТПА в списке — совместима', () => {
    expect(isMoldCompatibleWithImm({ compatibleImms: [{ id: 'imm-1' }] }, 'imm-1')).toBe(true)
  })
  it('ТПА не в списке — несовместима', () => {
    expect(isMoldCompatibleWithImm({ compatibleImms: [{ id: 'imm-2' }] }, 'imm-1')).toBe(false)
  })
})

describe('immOptionLabel / formatCompatibleImms', () => {
  it('архивный ТПА помечается «(архив)»', () => {
    expect(immOptionLabel({ name: 'ТПА-1', isActive: false })).toBe('ТПА-1 (архив)')
    expect(immOptionLabel({ name: 'ТПА-1', isActive: true })).toBe('ТПА-1')
  })
  it('список — имена через запятую', () => {
    expect(formatCompatibleImms([
      { name: 'ТПА-1', isActive: true },
      { name: 'ТПА-2', isActive: false }
    ])).toBe('ТПА-1, ТПА-2 (архив)')
  })
  it('пустой или отсутствующий список — пустая строка', () => {
    expect(formatCompatibleImms([])).toBe('')
    expect(formatCompatibleImms(undefined)).toBe('')
  })
})
```

- [ ] **Step 2: Убедиться, что падает**

Run: `npx vitest run src/utils/__tests__/moldCompatibility.spec.js`
Expected: FAIL — модуль `../moldCompatibility` не найден.

- [ ] **Step 3: Реализация утилиты**

`src/utils/moldCompatibility.js`:

```js
// PZP-16 (ADR-0018): совместимость ПФ↔ТПА — ручной список в карточке ПФ.
// Пустой список — совместимость не указана, ПФ подходит к любому ТПА.
export function isMoldCompatibleWithImm(mold, immId) {
  const list = mold?.compatibleImms ?? []
  return list.length === 0 || list.some(imm => imm.id === immId)
}

export function immOptionLabel(imm) {
  return imm.isActive ? imm.name : `${imm.name} (архив)`
}

export function formatCompatibleImms(list) {
  return (list ?? []).map(immOptionLabel).join(', ')
}
```

- [ ] **Step 4: Тесты утилиты проходят**

Run: `npx vitest run src/utils/__tests__/moldCompatibility.spec.js`
Expected: PASS, 7 тестов.

- [ ] **Step 5: Падающие тесты справочника**

`src/views/dictionary/__tests__/MoldDictionary.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import MoldDictionary from '../MoldDictionary.vue'

vi.mock('@/api/molds', () => ({
  moldsApi: {
    getList: vi.fn(() => Promise.resolve({ data: [] })),
    create: vi.fn(() => Promise.resolve({ data: {} })),
    update: vi.fn(() => Promise.resolve({ data: {} })),
    getQr: vi.fn(() => Promise.resolve({ data: {} }))
  }
}))
vi.mock('@/api/productTypes', () => ({
  productTypesApi: {
    getList: vi.fn(() => Promise.resolve({ data: [{ id: 'pt-1', article: 'ART', name: 'Изделие' }] }))
  }
}))
vi.mock('@/api/imm', () => ({
  immApi: {
    getList: vi.fn(() => Promise.resolve({ data: [
      { id: 'imm-1', name: 'ТПА-1', isActive: true },
      { id: 'imm-2', name: 'ТПА-2', isActive: true }
    ] }))
  }
}))

import { moldsApi } from '@/api/molds'
import { immApi } from '@/api/imm'

const moldRow = {
  id: 'mold-1', formId: 'PF-1', name: 'Форма', cavities: 2, partWeightGrams: 1, runnerWeightGrams: 1,
  maxResourceCycles: 1000, remainingResource: 1000, to1Cycles: 0, to2Cycles: 0, storageLocationIndex: '',
  isActive: true, productTypeId: 'pt-1', productTypeArticle: 'ART', productTypeName: 'Изделие',
  compatibleImms: [
    { id: 'imm-1', name: 'ТПА-1', isActive: true },
    { id: 'imm-9', name: 'ТПА-9', isActive: false }
  ]
}

function mountView() {
  return mount(MoldDictionary, { global: { plugins: [ElementPlus] } })
}

describe('MoldDictionary — совместимые ТПА (PZP-16)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('грузит только активные ТПА для выбора', async () => {
    mountView()
    await flushPromises()
    expect(immApi.getList).toHaveBeenCalledWith({ isActive: true })
  })

  it('при создании список пустой, опции — только активные ТПА', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.showCreateModal()
    expect(wrapper.vm.form.compatibleImmIds).toEqual([])
    expect(wrapper.vm.selectableImms.map(i => i.id)).toEqual(['imm-1', 'imm-2'])
  })

  it('при редактировании подставляет список ПФ и добавляет в опции её архивный ТПА', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editMold(moldRow)
    expect(wrapper.vm.form.compatibleImmIds).toEqual(['imm-1', 'imm-9'])
    expect(wrapper.vm.selectableImms.map(i => i.id)).toEqual(['imm-1', 'imm-2', 'imm-9'])
  })

  it('сохранение отправляет compatibleImmIds', async () => {
    const wrapper = mountView()
    await flushPromises()

    wrapper.vm.editMold(moldRow)
    await flushPromises()
    wrapper.vm.form.compatibleImmIds = ['imm-2']
    await wrapper.vm.saveMold()
    await flushPromises()

    expect(moldsApi.update).toHaveBeenCalledWith('mold-1',
      expect.objectContaining({ compatibleImmIds: ['imm-2'] }))
  })

  it('архивация ПФ не трогает список совместимых ТПА', async () => {
    const wrapper = mountView()
    await flushPromises()

    const { ElMessageBox } = await import('element-plus')
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValueOnce('confirm')
    await wrapper.vm.deleteMold(moldRow)
    await flushPromises()

    expect(moldsApi.update).toHaveBeenCalledWith('mold-1', { isActive: false })
  })
})
```

- [ ] **Step 6: Убедиться, что падает**

Run: `npx vitest run src/views/dictionary/__tests__/MoldDictionary.spec.js`
Expected: FAIL — `immApi.getList` не вызывается, `form.compatibleImmIds`/`selectableImms` undefined. (Тест архивации может пройти уже сейчас — это регрессионная страховка.)

- [ ] **Step 7: Реализация в `MoldDictionary.vue`**

Шаблон, таблица — после колонки «Тип изделия» (перед `<el-table-column prop="cavities" ...>`):

```vue
      <el-table-column label="ТПА" width="180">
        <template #default="{ row }">
          <span v-if="row.compatibleImms?.length">{{ formatCompatibleImms(row.compatibleImms) }}</span>
          <span v-else class="text-gray-400">любой</span>
        </template>
      </el-table-column>
```

Шаблон, диалог — перед `<el-form-item label="Статус">`:

```vue
        <el-form-item label="Совместимые ТПА">
          <el-select
            v-model="form.compatibleImmIds"
            multiple
            filterable
            clearable
            placeholder="Любой ТПА"
            class="w-full"
          >
            <el-option
              v-for="imm in selectableImms"
              :key="imm.id"
              :label="immOptionLabel(imm)"
              :value="imm.id"
            />
          </el-select>
          <div class="text-xs text-gray-500">Пусто — подходит к любому ТПА</div>
        </el-form-item>
```

Скрипт:

- импорты: `import { immApi } from '@/api/imm'` и `import { immOptionLabel, formatCompatibleImms } from '@/utils/moldCompatibility'`;
- после `const productTypes = ref([])`:

```js
const imms = ref([])

// PZP-16: опции — активные ТПА плюс уже привязанные к редактируемой ПФ архивные.
// Архивные не disabled: тег отключённой опции в Element Plus снять нельзя, а повторно
// добавить ранее привязанный архивный ТПА сервер разрешает.
const selectableImms = computed(() => {
  const linkedArchived = (editingMold.value?.compatibleImms ?? [])
    .filter(i => !i.isActive && !imms.value.some(a => a.id === i.id))
  return [...imms.value, ...linkedArchived]
})
```

- в `reactive form` и в `Object.assign` внутри `showCreateModal` — поле `compatibleImmIds: [],`;
- в `Object.assign` внутри `editMold` — `compatibleImmIds: (mold.compatibleImms ?? []).map(i => i.id),`;
- после `loadProductTypes`:

```js
const loadImms = async () => {
  try {
    const { data } = await immApi.getList({ isActive: true })
    imms.value = data
  } catch (error) {
    ElMessage.error('Ошибка загрузки ТПА')
  }
}
```

- `onMounted`: `await Promise.all([loadMolds(), loadProductTypes(), loadImms()])`.

`saveMold` и `deleteMold` не меняются: `form` уже содержит `compatibleImmIds`, а `deleteMold` шлёт только `{ isActive: false }` (сервер не трогает список при `null`).

- [ ] **Step 8: Тесты проходят**

Run: `npx vitest run src/utils/__tests__/moldCompatibility.spec.js src/views/dictionary/__tests__/MoldDictionary.spec.js`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/utils/moldCompatibility.js src/utils/__tests__/moldCompatibility.spec.js src/views/dictionary/MoldDictionary.vue src/views/dictionary/__tests__/MoldDictionary.spec.js
git commit -m "feat(mold-ui): compatible IMMs field and column in mold dictionary (PZP-16)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Фронт — фильтр совместимости в форме задания

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue`
- Test: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js`

**Interfaces:**
- Consumes: `isMoldCompatibleWithImm` (Task 4).
- Produces (для тестов через `wrapper.vm`): computed `availableMolds`, `availableImms`, `moldNoDataText`.

- [ ] **Step 1: Падающие тесты**

В конец `src/views/tasks/__tests__/TaskFormModal.spec.js` добавить (в начале файла `immApi` уже замокан; добавить импорт `import { immApi } from '@/api/imm'` рядом с другими импортами моков):

```js
describe('TaskFormModal — совместимость ПФ↔ТПА (PZP-16)', () => {
  const compatMolds = [
    { id: 'mold-any', name: 'Любая', cavities: 1, isActive: true, productTypeId: 'pt-X', compatibleImms: [] },
    { id: 'mold-1', name: 'Только ТПА-1', cavities: 1, isActive: true, productTypeId: 'pt-X',
      compatibleImms: [{ id: 'imm-1', name: 'ТПА-1', isActive: true }] },
    { id: 'mold-2', name: 'Только ТПА-2', cavities: 1, isActive: true, productTypeId: 'pt-Y',
      compatibleImms: [{ id: 'imm-2', name: 'ТПА-2', isActive: true }] }
  ]
  const compatImms = [
    { id: 'imm-1', name: 'ТПА-1', isActive: true },
    { id: 'imm-2', name: 'ТПА-2', isActive: true }
  ]

  // mockResolvedValue переживает vi.clearAllMocks — блок должен оставаться последним в файле,
  // либо добавить mockReset в afterEach, если после него появятся другие describe.
  beforeEach(() => {
    vi.clearAllMocks()
    moldsApi.getList.mockResolvedValue({ data: compatMolds })
    immApi.getList.mockResolvedValue({ data: compatImms })
  })

  async function mountLoaded(props = {}) {
    const wrapper = mountModal(props)
    await wrapper.vm.loadMolds()
    await wrapper.vm.loadImms()
    await flushPromises()
    return wrapper
  }

  it('без выбранного ТПА список ПФ полный', async () => {
    const wrapper = await mountLoaded()
    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-any', 'mold-1', 'mold-2'])
  })

  it('после выбора ТПА остаются совместимые ПФ и ПФ с пустым списком', async () => {
    const wrapper = await mountLoaded()
    wrapper.vm.form.immId = 'imm-1'
    await flushPromises()
    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-any', 'mold-1'])
  })

  it('после выбора ПФ со списком ТПА ограничены этим списком', async () => {
    const wrapper = await mountLoaded()
    wrapper.vm.form.moldId = 'mold-2'
    await flushPromises()
    expect(wrapper.vm.availableImms.map(i => i.id)).toEqual(['imm-2'])
  })

  it('ПФ с пустым списком не ограничивает ТПА', async () => {
    const wrapper = await mountLoaded()
    wrapper.vm.form.moldId = 'mold-any'
    await flushPromises()
    expect(wrapper.vm.availableImms.map(i => i.id)).toEqual(['imm-1', 'imm-2'])
  })

  it('фильтр по ТПА сочетается с фильтром по типу изделия заказа', async () => {
    const wrapper = await mountLoaded({ lockedOrder: { id: 'order-9', number: 'ORD-9', productTypeId: 'pt-X', productTypeArticle: 'ART-X' } })
    wrapper.vm.form.immId = 'imm-2'
    await flushPromises()
    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-any'])
  })

  it('пустой список ПФ при выбранном ТПА объясняет причину', async () => {
    moldsApi.getList.mockResolvedValue({ data: [compatMolds[2]] })
    const wrapper = await mountLoaded()
    wrapper.vm.form.immId = 'imm-1'
    await flushPromises()
    expect(wrapper.vm.availableMolds).toEqual([])
    expect(wrapper.vm.moldNoDataText).toBe('Нет пресс-форм, совместимых с этим ТПА')
  })

  it('с заказом и выбранным ТПА текст называет оба условия', async () => {
    const wrapper = await mountLoaded({ lockedOrder: { id: 'order-9', number: 'ORD-9', productTypeId: 'pt-X', productTypeArticle: 'ART-X' } })
    wrapper.vm.form.immId = 'imm-1'
    await flushPromises()
    expect(wrapper.vm.moldNoDataText).toBe('Нет пресс-форм этого типа изделия, совместимых с этим ТПА')
  })

  it('в режиме редактирования фильтр не применяется — ПФ задания остаётся в опциях', async () => {
    const task = { id: 'task-1', immId: 'imm-2', moldId: 'mold-1', planQuantity: 10, status: 'Planned' }
    const wrapper = await mountLoaded({ task })
    expect(wrapper.vm.availableMolds.map(m => m.id)).toContain('mold-1')
    expect(wrapper.vm.availableImms.map(i => i.id)).toContain('imm-2')
  })
})
```

- [ ] **Step 2: Убедиться, что падает**

Run: `npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: новые тесты FAIL (нет фильтра, `availableImms` undefined); прежние — PASS.

- [ ] **Step 3: Реализация в `TaskFormModal.vue`**

Импорт: `import { isMoldCompatibleWithImm } from '@/utils/moldCompatibility'`.

Шаблон: в селекте ТПА `v-for="imm in imms"` → `v-for="imm in availableImms"`.

Скрипт — заменить блок `availableMolds` и `moldNoDataText` на:

```js
// PZP-16: при создании выбор ПФ и ТПА фильтруется по совместимости (пустой список ПФ — любой ТПА).
// При редактировании ТПА и ПФ заблокированы — фильтр не применяем, иначе ПФ «несовместимого»
// задания выпала бы из опций и селект показал бы сырой ID.
const filterByCompatibility = computed(() => !editingTask.value)

const availableMolds = computed(() => {
  let list = props.lockedOrder
    ? molds.value.filter(m => m.productTypeId === props.lockedOrder.productTypeId)
    : molds.value
  if (filterByCompatibility.value && form.immId) {
    list = list.filter(m => isMoldCompatibleWithImm(m, form.immId))
  }
  return list
})

const availableImms = computed(() => {
  if (!filterByCompatibility.value || !selectedMoldInfo.value) return imms.value
  return imms.value.filter(imm => isMoldCompatibleWithImm(selectedMoldInfo.value, imm.id))
})

// Пустой список ПФ — не сбой: нет форм типа изделия заказа и/или совместимых с ТПА;
// без обоих условий остаётся стандартный текст Element Plus.
const moldNoDataText = computed(() => {
  const byImm = filterByCompatibility.value && !!form.immId
  if (props.lockedOrder) {
    if (byImm) return 'Нет пресс-форм этого типа изделия, совместимых с этим ТПА'
    const article = props.lockedOrder.productTypeArticle
    return `Нет активных пресс-форм для типа изделия ${article || 'заказа'}`
  }
  return byImm ? 'Нет пресс-форм, совместимых с этим ТПА' : undefined
})
```

- [ ] **Step 4: Тесты проходят**

Run: `npx vitest run src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: PASS — все, включая прежние тесты `lockedOrder` (тексты без выбранного ТПА не изменились).

- [ ] **Step 5: Весь фронтовый набор**

Run: `npm test`
Expected: PASS без падений.

- [ ] **Step 6: Commit**

```bash
git add src/views/tasks/TaskFormModal.vue src/views/tasks/__tests__/TaskFormModal.spec.js
git commit -m "feat(task-ui): filter molds and IMMs by compatibility in task form (PZP-16)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Документация — ADR-0018, CLAUDE.md, домен, руководство

**Files:**
- Create: `docs/adr/0018-mold-imm-compatibility-manual-list.md`
- Modify: `docs/adr/README.md` (индекс, после строки ADR-0017)
- Modify: `CLAUDE.md` (раздел «Инварианты домена», после подраздела «Тип изделия»)
- Create: `docs/domain/mold.md`
- Modify: `docs/domain/README.md` (каталог сущностей: `mold.md` 🔲 → ✅)
- Modify: `docs/user-guide/manager.md` (§5.3 таблица полей, §9.3)

- [ ] **Step 1: ADR-0018**

```markdown
# ADR-0018: Совместимость ПФ↔ТПА — ручной список в карточке ПФ

- **Статус:** Accepted
- **Дата:** 2026-10-15

## Контекст

Пресс-форма ставится не на любой ТПА (габариты, усилие смыкания, ход, посадка). Система этого не
знала: задание создавалось для любой пары, ошибка обнаруживалась в цеху. Планированию на Гантте
(PZP-07) нужно знать, на какие ТПА можно переносить задание. Исходная идея PZP-07 — физические
параметры ПФ и ТПА и автоматический чекер — требует заводить параметры в оба справочника и
согласовывать правила совместимости.

## Решение

- У ПФ — ручной список совместимых ТПА: связь many-to-many, таблица `MoldCompatibleImms
  (MoldId, ImmId)`, навигация только `Mold.CompatibleImms`.
- **Пустой список = совместимость не указана = ПФ подходит к любому ТПА.**
- Несовместимую пару задания создать нельзя: форма фильтрует выбор, API отвечает 400.
  Проверка только при создании — ТПА и ПФ задания после создания неизменяемы.
- Архивный ТПА добавить в список нельзя; ранее привязанный остаётся (как архивный тип изделия,
  ADR-0007).
- Существующие задания, ставшие «несовместимыми» после правки списка, не помечаются и не
  блокируются.

## Отвергнутые альтернативы

- **Пустой список = «ни с одним ТПА».** После выката на пилот ПЗП задания перестали бы
  создаваться, пока не заполнены списки всех ПФ.
- **Обязательный список с автозаполнением по истории заданий.** Миграция данных и лишняя работа
  ради строгости, которая сейчас не нужна.
- **Только предупреждение / разрешить с подтверждением.** Список становится рекомендацией —
  планировщик не может на него опереться.
- **Физические параметры + автоматический чекер.** Дорого сейчас; может лечь поверх списка позже
  (например, как подсказка при заполнении).
- **`jsonb Guid[]` в `Mold`.** Нет целостности FK, фильтр по ТПА — через JSON-операторы.
- **Явная сущность связи с автором/датой.** Аудит связи не востребован.

## Последствия

- Правило — `Core/Policies/MoldImmCompatibility`; его используют `CreateTask` и (на фронте)
  `utils/moldCompatibility.js`.
- Обратного вида в карточке ТПА нет; данные позволяют добавить его без изменения схемы.
- PZP-07 получает готовый примитив совместимости для подсказок на Гантте.
```

- [ ] **Step 2: Индекс ADR**

В `docs/adr/README.md` после строки ADR-0017 добавить:

```markdown
- [ADR-0018](0018-mold-imm-compatibility-manual-list.md) — Совместимость ПФ↔ТПА: ручной список в карточке ПФ
```

- [ ] **Step 3: CLAUDE.md**

После подраздела «### Тип изделия (`ProductType`) обязателен для новых ПФ и заданий» (перед «### Плановое начало и выдача задания») вставить:

```markdown
### Совместимые ТПА пресс-формы

`Mold.CompatibleImms` (таблица `MoldCompatibleImms`) — ручной список ТПА, на которые ставится ПФ.
**Пусто = совместимость не указана = любой ТПА.** Задание с ТПА вне непустого списка создать нельзя
(`MoldImmCompatibility.IsCompatible`, проверка в `CreateTask`). Архивный ТПА в список не добавить,
ранее привязанный остаётся. Существующие задания при правке списка не трогаются. См. ADR-0018.
```

- [ ] **Step 4: `docs/domain/mold.md`**

Скопировать структуру `docs/domain/_TEMPLATE.md` (образец заполнения — `docs/domain/imm.md`) и заполнить:

```markdown
# Пресс-форма (`Mold`)

> Оснастка, которую ставят на ТПА для выпуска изделия. Определяет, что и сколько деталей
> отливается за цикл, и копит наработку до ТО и списания.

## Что это

Пресс-форма (ПФ) — сменная оснастка ТПА. Технолог/руководитель ведёт её в справочнике: тип
изделия, гнёздность, веса, ресурс, пороги ТО, место хранения и список ТПА, на которые её можно
поставить. Наладчик ставит ПФ на ТПА в начале задания; циклы задания засчитываются в наработку ПФ.

## Код

- Сущность: `Wintime.Control.Core.Entities.Mold`
- Связанные: `ProductType` (тип изделия), `Imm` (совместимые ТПА), `ShiftTask`, `ImmCycle`

## Ключевые поля

| Поле | Смысл и правила |
|---|---|
| `FormId` | Артикул ПФ, уникален, кодируется в QR |
| `ProductTypeId` | Что выпускает ПФ. Обязателен для новых ПФ и для заданий; сменить нельзя, если по ПФ есть задания (кроме первого присвоения) — ADR-0007 |
| `Cavities` | Текущая гнёздность; меняется при заглушке гнёзд. Для истории — снапшот `ImmCycle.Cavities` |
| `MaxResourceCycles`, `To1Cycles`, `To2Cycles` | Ресурс и пороги ТО в смыканиях |
| `CompatibleImms` | ТПА, на которые ставится ПФ. Пусто — любой ТПА. Задание с ТПА вне непустого списка не создать — ADR-0018 |
| `IsActive` | Архивный флаг: `false` — ПФ выведена из оборота, история сохраняется |

## Инварианты (нарушение = баг)

- ПФ не удаляют физически — только `IsActive = false`. → CLAUDE.md
- Историю считают по снапшоту `ImmCycle.Cavities`, а не по текущему `Cavities`. → CLAUDE.md
- Пустой список совместимых ТПА = любой ТПА; несовместимую пару задания не создать; архивный ТПА
  в список не добавить, ранее привязанный остаётся. → [ADR-0018](../adr/0018-mold-imm-compatibility-manual-list.md)

## Связанные

- ADR: [ADR-0007](../adr/0007-product-type-catalog.md) — каталог изделий; [ADR-0018](../adr/0018-mold-imm-compatibility-manual-list.md) — совместимые ТПА
- Спека: `docs/superpowers/specs/2026-10-15-pzp-16-mold-compatible-imms-design.md`
- Сущности домена: [`imm.md`](imm.md), [`shift-task.md`](shift-task.md)
```

В `docs/domain/README.md`, раздел «Каталог сущностей», строку
``- 🔲 `mold.md` — Пресс-форма (`Mold`): гнёздность, ресурс, ТО, QR-верификация`` заменить на
``- ✅ [`mold.md`](mold.md) — Пресс-форма (`Mold`): гнёздность, ресурс, ТО, совместимые ТПА``.

- [ ] **Step 5: Руководство руководителя**

В `docs/user-guide/manager.md`:

1. §5.3, таблица полей: строку «**Пресс-форма**» заменить на

```markdown
| **Пресс-форма** | В списке указано число гнёзд. Архивные ПФ выбрать нельзя. ПФ должна иметь тип изделия. После выбора ТПА в списке остаются только совместимые с ним ПФ и ПФ, у которых совместимые ТПА не указаны. Выбор ПФ так же ограничивает список ТПА |
```

2. §9.3, таблица полей: после строки «**ТО-1**, **ТО-2 (смыкания)**» добавить

```markdown
| **Совместимые ТПА** | На какие ТПА можно поставить форму. Пусто — на любой. Задание с ТПА не из списка создать нельзя. Архивный ТПА добавить нельзя; если ТПА ушёл в архив после привязки, он остаётся в списке с пометкой «(архив)» |
```

3. §9.3, после абзаца о колонке «Остаток ресурса» (перед «Кнопка **«Удалить»**…») добавить:

```markdown
В колонке **ТПА** перечислены совместимые ТПА; «любой» — список не заполнен. Изменение списка
не затрагивает уже созданные задания.
```

- [ ] **Step 6: Commit**

```bash
git add docs/adr/0018-mold-imm-compatibility-manual-list.md docs/adr/README.md CLAUDE.md docs/domain/mold.md docs/domain/README.md docs/user-guide/manager.md
git commit -m "docs: ADR-0018 mold-IMM compatibility, domain and user guide (PZP-16)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Финальная проверка

- [ ] **Step 1: Сборка и все .NET-тесты**

Run (из корня): `dotnet build Wintime.Control.sln` → `0 Error(s)`;
`dotnet test Wintime.Control.Tests.Unit` и `dotnet test Wintime.Control.Tests.Integration` → PASS.

- [ ] **Step 2: Фронт**

Run (из `Wintime-Control-Frontend`): `npm test` → PASS; `npm run build` → успешная сборка.

- [ ] **Step 3: Ручная проверка (по возможности)**

`dotnet run --project Wintime.Control.API` + `npm run dev`: в справочнике ПФ задать ТПА-1 у формы → колонка «ТПА» показывает ТПА-1; в «Новом задании» выбрать другой ТПА → формы нет в списке; очистить список → форма снова видна.
