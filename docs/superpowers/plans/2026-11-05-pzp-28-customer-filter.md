# PZP-28 «Фильтр по заказчику» Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Добавить серверный фильтр по одному заказчику и пункт «(не указано)» в типы изделий, пресс-формы и заказы, используя существующие подсказки PZP-23.

**Architecture:** Три существующих GET-контроллера получают одинаковую пару query-параметров `customer`/`customerEmpty` и добавляют точный `Where` к текущим фильтрам. На фронте один `CustomerFilterSelect` управляет трёхзначным состоянием фильтра и запрашивает существующий `suggestCompanies`; каждый view только хранит состояние и передаёт параметры в свой уже существующий `getList(params)`.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, PostgreSQL 16, xUnit + FluentAssertions, Vue 3, Element Plus, Vitest + jsdom.

## Global Constraints

- Спека — источник решений: `docs/superpowers/specs/2026-11-05-pzp-28-customer-filter-design.md`.
- Источник названий — только существующий `GET /api/producttypes/suggestions/companies`; новый endpoint и таблица заказчиков не нужны.
- Фильтр одиночный: пусто = все; `customer=<точное значение>` = конкретный заказчик; `customerEmpty=true` = заказчик не указан.
- Совпадение `customer` — точное и регистрозависимое; `ILike` остаётся только в поиске подсказок.
- Заказы фильтруются только по `Order.Customer`, без fallback к `ProductType.Company`; ПФ — через тип, а ПФ без типа входит в `customerEmpty=true`.
- Одновременные непустой `customer` и `customerEmpty=true` → 400 с текстом «Параметры customer и customerEmpty нельзя передавать вместе.».
- Новые фильтры работают через И со всеми существующими фильтрами; DTO, права и сортировки не меняются.
- Миграции, новые NuGet/npm-пакеты, новые колонки таблиц и изменения `SuggestInput.vue` запрещены объёмом задачи.
- Начинать от актуального `master` после мержа PZP-23; ветка `feature/pzp-28-customer-filter`, в `master` напрямую не пушить.
- Перед PR: `VERSION` `1.23.0` → `1.24.0`.
- Коммит-сообщения заканчиваются отдельным абзацем `Co-Authored-By: Kimi <noreply@moonshot.ai>`.

**Рабочая папка всех backend-команд:** `E:\Projects\Control\Sources\Control`.

---

## File Structure

**Создаются:**

| Файл | Ответственность |
|---|---|
| `Wintime.Control.Tests.Integration/Molds/MoldCustomerFilterTests.cs` | API-фильтр ПФ через тип, включая старую ПФ без типа |
| `Wintime.Control.Tests.Integration/Orders/OrderCustomerFilterTests.cs` | API-фильтр заказа без fallback к компании изделия |
| `Wintime-Control-Frontend/src/components/common/CustomerFilterSelect.vue` | Одиночный remote-select с состояниями все/значение/не указано |
| `Wintime-Control-Frontend/src/components/common/__tests__/CustomerFilterSelect.spec.js` | Поведение общего компонента |

**Изменяются:**

| Файл | Что |
|---|---|
| `Wintime.Control.API/Controllers/ProductTypesController.cs` | Параметры и `Where` фильтра типов |
| `Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs` | Integration-тесты фильтра типов |
| `Wintime.Control.API/Controllers/MoldsController.cs` | Параметры и `Where` фильтра ПФ |
| `Wintime.Control.API/Controllers/OrdersController.cs` | Параметры и `Where` фильтра заказов |
| `Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue` | Общий фильтр и query-параметры |
| `Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue` | Общий фильтр, `fetchCompanies`, query-параметры |
| `Wintime-Control-Frontend/src/views/OrdersView.vue` | Общий фильтр и query-параметры |
| Три существующих Vitest-файла этих views | Проверка передачи параметров и совместимости с текущими фильтрами |
| `docs/user-guide/manager.md` | Описание фильтра в трёх разделах |
| `VERSION` | `1.24.0` |

---

### Task 1: Backend — фильтр типов изделий

**Files:**
- Modify: `Wintime.Control.API/Controllers/ProductTypesController.cs:95-118`
- Test: `Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs`

**Interfaces:**
- Consumes: `CustomerRules.Clean(string?) → string?`, существующие `ProductTypeDto`, integration-фабрика.
- Produces: `GET /api/producttypes?isActive=&search=&customer=&customerEmpty=`; те же DTO и роли.

- [x] **Step 1: Добавить failing integration-тесты**

Добавить в `ProductTypesControllerTests` следующий хелпер и тесты. `ProductType`, `Product`, `ControlDbContext`, `CreateScope` уже импортированы в существующем файле.

```csharp
private async Task<ProductType> SeedCustomerTypeAsync(
    string marker, string? company, bool isActive = true)
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    var article = $"CF-{marker}-{Guid.NewGuid():N}".ToUpperInvariant();
    var type = new ProductType
    {
        Article = article, Name = $"Тип {marker}", Company = company, IsActive = isActive
    };
    type.Products.Add(new Product
    {
        Article = "0", Name = "По умолчанию", FullArticle = article + ".0", IsActive = true
    });
    db.ProductTypes.Add(type);
    await db.SaveChangesAsync();
    return type;
}

[Fact]
public async Task List_FiltersByCustomer()
{
    var client = await ManagerClientAsync();
    var marker = Guid.NewGuid().ToString("N")[..12];
    var customer = $"Заказчик {marker}";
    var match = await SeedCustomerTypeAsync(marker, customer);
    var other = await SeedCustomerTypeAsync(marker, $"Другой {marker}");
    var differentCase = await SeedCustomerTypeAsync(marker, customer.ToUpperInvariant());

    var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
        $"/api/producttypes?customer={Uri.EscapeDataString(customer)}");

    list!.Should().ContainSingle(t => t.Id == match.Id);
    list.Should().NotContain(t => t.Id == other.Id || t.Id == differentCase.Id);
}

[Fact]
public async Task List_FiltersUnspecifiedCustomer()
{
    var client = await ManagerClientAsync();
    var marker = Guid.NewGuid().ToString("N")[..12];
    var empty = await SeedCustomerTypeAsync(marker, null);
    var named = await SeedCustomerTypeAsync(marker, $"Заказчик {marker}");

    var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
        $"/api/producttypes?search={marker}&customerEmpty=true");

    list!.Should().ContainSingle(t => t.Id == empty.Id);
    list.Should().NotContain(t => t.Id == named.Id);
}

[Fact]
public async Task List_CustomerFilterCombinesWithExistingFilters()
{
    var client = await ManagerClientAsync();
    var marker = Guid.NewGuid().ToString("N")[..12];
    var customer = $"Заказчик {marker}";
    var active = await SeedCustomerTypeAsync(marker, customer, isActive: true);
    var archived = await SeedCustomerTypeAsync(marker, customer, isActive: false);
    var archivedOther = await SeedCustomerTypeAsync(marker, $"Другой {marker}", isActive: false);

    var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
        $"/api/producttypes?isActive=false&customer={Uri.EscapeDataString(customer)}");

    list!.Should().ContainSingle(t => t.Id == archived.Id);
    list.Should().NotContain(t => t.Id == active.Id || t.Id == archivedOther.Id);
}

[Fact]
public async Task List_UnknownCustomer_ReturnsEmpty()
{
    var client = await ManagerClientAsync();
    var unknown = $"Несуществующий {Guid.NewGuid():N}";

    var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
        $"/api/producttypes?customer={Uri.EscapeDataString(unknown)}");

    list.Should().BeEmpty();
}

[Fact]
public async Task List_CustomerAndCustomerEmpty_ReturnsBadRequest()
{
    var client = await ManagerClientAsync();

    var response = await client.GetAsync(
        "/api/producttypes?customer=ООО%20Ромашка&customerEmpty=true");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync()).Should().Contain(
        "Параметры customer и customerEmpty нельзя передавать вместе.");
}
```

- [x] **Step 2: Запустить тесты и увидеть FAIL**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductTypesControllerTests"
```

Expected: новые тесты FAIL: текущий action игнорирует неизвестные query-параметры и возвращает нефильтрованный список/200 вместо 400. Существующие тесты остаются PASS.

- [x] **Step 3: Реализовать параметры и фильтр**

Заменить `GetList` в `ProductTypesController` на:

```csharp
/// <summary>Список типов изделий (активность, поиск и точный фильтр по заказчику).</summary>
[HttpGet]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
public async Task<ActionResult<IEnumerable<ProductTypeDto>>> GetList(
    [FromQuery] bool? isActive = null,
    [FromQuery] string? search = null,
    [FromQuery] string? customer = null,
    [FromQuery] bool customerEmpty = false)
{
    var customerFilter = CustomerRules.Clean(customer);
    if (customerEmpty && customerFilter != null)
        return BadRequest("Параметры customer и customerEmpty нельзя передавать вместе.");

    var query = _context.ProductTypes
        .Include(t => t.Products).ThenInclude(p => p.RecipeIngredients).ThenInclude(i => i.Material)
        .AsQueryable();

    if (isActive.HasValue)
        query = query.Where(p => p.IsActive == isActive.Value);
    if (!string.IsNullOrEmpty(search))
    {
        var pattern = $"%{search}%";
        query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Article, pattern)
            || p.Products.Any(x => EF.Functions.ILike(x.FullArticle, pattern) || EF.Functions.ILike(x.Name, pattern)));
    }
    if (customerFilter != null)
        query = query.Where(p => p.Company == customerFilter);
    else if (customerEmpty)
        query = query.Where(p => p.Company == null);

    var items = await query.OrderBy(p => p.Article).ToListAsync();
    var usedIds = await UsedProductIdsAsync(items.SelectMany(t => t.Products).Select(p => p.Id).ToList());
    return Ok(items.Select(t => ToDto(t, usedIds)).ToList());
}
```

- [x] **Step 4: Запустить тесты и увидеть PASS**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ProductTypesControllerTests"
```

Expected: все тесты класса PASS, 0 failed.

- [x] **Step 5: Commit**

```powershell
git add Wintime.Control.API/Controllers/ProductTypesController.cs Wintime.Control.Tests.Integration/ProductTypes/ProductTypesControllerTests.cs
git commit -m "feat(producttypes): фильтр списка по заказчику (PZP-28)" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 2: Backend — фильтр пресс-форм через тип изделия

**Files:**
- Modify: `Wintime.Control.API/Controllers/MoldsController.cs:25-59`
- Create: `Wintime.Control.Tests.Integration/Molds/MoldCustomerFilterTests.cs`

**Interfaces:**
- Consumes: контракт параметров Task 1, `CustomerRules.Clean`.
- Produces: `GET /api/molds?isActive=&search=&customer=&customerEmpty=`; `customerEmpty=true` включает `ProductTypeId == null`.

- [x] **Step 1: Создать failing integration-тесты**

Создать `Wintime.Control.Tests.Integration/Molds/MoldCustomerFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldCustomerFilterTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldCustomerFilterTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Mold> SeedMoldAsync(
        string marker, string? company, bool withType = true, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        ProductType? type = null;
        if (withType)
        {
            var article = $"MF-{marker}-{Guid.NewGuid():N}".ToUpperInvariant();
            type = new ProductType
            {
                Article = article, Name = $"Тип {marker}", Company = company, IsActive = true
            };
            type.Products.Add(new Product
            {
                Article = "0", Name = "По умолчанию", FullArticle = article + ".0", IsActive = true
            });
        }
        var mold = new Mold
        {
            FormId = $"PF-{Guid.NewGuid():N}", Name = $"ПФ {marker}", Cavities = 1,
            IsActive = isActive, ProductType = type
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold;
    }

    private async Task SeedOrderWithCustomerAsync(string customer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.Orders.Add(new Order
        {
            Number = $"ORD-{Guid.NewGuid():N}", Customer = customer,
            DueDate = DateTime.UtcNow.Date.AddDays(7),
            ProductId = _factory.TestProductId, Quantity = 1
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_FiltersByProductTypeCompany()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var match = await SeedMoldAsync(marker, customer);
        var other = await SeedMoldAsync(marker, $"Другой {marker}");
        var withoutType = await SeedMoldAsync(marker, null, withType: false);

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?customer={Uri.EscapeDataString(customer)}");

        list!.Should().ContainSingle(m => m.Id == match.Id);
        list.Should().NotContain(m => m.Id == other.Id || m.Id == withoutType.Id);
    }

    [Fact]
    public async Task List_FiltersUnspecifiedCustomer()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var noCompany = await SeedMoldAsync(marker, null);
        var noType = await SeedMoldAsync(marker, null, withType: false);
        var named = await SeedMoldAsync(marker, $"Заказчик {marker}");

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?search={marker}&customerEmpty=true");

        list!.Should().Contain(m => m.Id == noCompany.Id);
        list.Should().Contain(m => m.Id == noType.Id);
        list.Should().NotContain(m => m.Id == named.Id);
    }

    [Fact]
    public async Task List_OrderOnlyCustomer_ReturnsEmpty()
    {
        var client = await ManagerClientAsync();
        var orderOnly = $"Только заказ {Guid.NewGuid():N}";
        await SeedOrderWithCustomerAsync(orderOnly);

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?customer={Uri.EscapeDataString(orderOnly)}");

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_CustomerFilterCombinesWithExistingFilters()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var active = await SeedMoldAsync(marker, customer, isActive: true);
        var archived = await SeedMoldAsync(marker, customer, isActive: false);
        var archivedOther = await SeedMoldAsync(marker, $"Другой {marker}", isActive: false);

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?isActive=false&search={marker}&customer={Uri.EscapeDataString(customer)}");

        list!.Should().ContainSingle(m => m.Id == archived.Id);
        list.Should().NotContain(m => m.Id == active.Id || m.Id == archivedOther.Id);
    }

    [Fact]
    public async Task List_CustomerAndCustomerEmpty_ReturnsBadRequest()
    {
        var client = await ManagerClientAsync();

        var response = await client.GetAsync(
            "/api/molds?customer=ООО%20Ромашка&customerEmpty=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [x] **Step 2: Запустить новый класс и увидеть FAIL**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~MoldCustomerFilterTests"
```

Expected: все пять тестов FAIL, потому что текущий `GetMoldList` игнорирует новые параметры.

- [x] **Step 3: Реализовать фильтр**

Заменить `GetMoldList` в `MoldsController` на:

```csharp
/// <summary>Список пресс-форм с фильтрами по активности, поиску и заказчику типа.</summary>
[HttpGet]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
public async Task<ActionResult<IEnumerable<MoldDto>>> GetMoldList(
    [FromQuery] bool? isActive = null,
    [FromQuery] string? search = null,
    [FromQuery] string? customer = null,
    [FromQuery] bool customerEmpty = false)
{
    var customerFilter = CustomerRules.Clean(customer);
    if (customerEmpty && customerFilter != null)
        return BadRequest("Параметры customer и customerEmpty нельзя передавать вместе.");

    var query = _context.Molds
        .Include(m => m.ProductType).ThenInclude(pt => pt!.Products)
        .Include(m => m.CompatibleImms)
        .AsQueryable();

    if (isActive.HasValue)
        query = query.Where(m => m.IsActive == isActive.Value);
    if (!string.IsNullOrEmpty(search))
        query = query.Where(m => m.Name.Contains(search) || m.FormId.Contains(search));
    if (customerFilter != null)
        query = query.Where(m => m.ProductType != null && m.ProductType.Company == customerFilter);
    else if (customerEmpty)
        query = query.Where(m => m.ProductType == null || m.ProductType.Company == null);

    var molds = await query.ToListAsync();
    var moldIds = molds.Select(m => m.Id).ToList();
    var cycleCounts = await _context.ImmCycles
        .Where(c => c.MoldId != null && moldIds.Contains(c.MoldId.Value) && c.IsSuccessful)
        .GroupBy(c => c.MoldId!.Value)
        .Select(g => new { MoldId = g.Key, Total = g.Count() })
        .ToListAsync();
    var cycleMap = cycleCounts.ToDictionary(x => x.MoldId, x => x.Total);
    var dtos = molds.Select(m => ToDto(m, cycleMap.GetValueOrDefault(m.Id))).ToList();
    return Ok(dtos);
}
```

- [x] **Step 4: Запустить новый класс и существующие mold integration-тесты**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Wintime.Control.Tests.Integration.Molds"
```

Expected: все тесты namespace `Molds` PASS, 0 failed.

- [x] **Step 5: Commit**

```powershell
git add Wintime.Control.API/Controllers/MoldsController.cs Wintime.Control.Tests.Integration/Molds/MoldCustomerFilterTests.cs
git commit -m "feat(molds): фильтр по заказчику типа изделия (PZP-28)" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 3: Backend — фильтр заказов без fallback к изделию

**Files:**
- Modify: `Wintime.Control.API/Controllers/OrdersController.cs:81-103`
- Create: `Wintime.Control.Tests.Integration/Orders/OrderCustomerFilterTests.cs`

**Interfaces:**
- Consumes: контракт параметров Task 1, `CustomerRules.Clean`.
- Produces: `GET /api/orders?status=&search=&productTypeId=&productId=&customer=&customerEmpty=`.

- [x] **Step 1: Создать failing integration-тесты**

Создать `Wintime.Control.Tests.Integration/Orders/OrderCustomerFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrderCustomerFilterTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public OrderCustomerFilterTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Order> SeedOrderAsync(
        string marker, string? customer, string? typeCompany,
        OrderStatus status = OrderStatus.Active, Guid? productId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        Product? product = null;
        if (productId == null)
        {
            var article = $"OF-{marker}-{Guid.NewGuid():N}".ToUpperInvariant();
            var type = new ProductType
            {
                Article = article, Name = $"Тип {marker}", Company = typeCompany, IsActive = true
            };
            product = new Product
            {
                Article = "0", Name = "По умолчанию", FullArticle = article + ".0",
                IsActive = true, ProductType = type
            };
        }
        var order = new Order
        {
            Number = $"ORD-{marker}-{Guid.NewGuid():N}", Customer = customer,
            DueDate = DateTime.UtcNow.Date.AddDays(7), Product = product,
            ProductId = productId ?? Guid.Empty, Quantity = 100, Status = status
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task List_FiltersByOrderCustomer_WithoutProductTypeFallback()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var ownCustomer = await SeedOrderAsync(marker, customer, $"Другая компания {marker}");
        var fallbackCandidate = await SeedOrderAsync(marker, null, customer);
        var other = await SeedOrderAsync(marker, $"Другой {marker}", null);

        var namedList = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?customer={Uri.EscapeDataString(customer)}", JsonOptions);
        namedList!.Should().ContainSingle(o => o.Id == ownCustomer.Id);
        namedList.Should().NotContain(o => o.Id == fallbackCandidate.Id || o.Id == other.Id);
    }

    [Fact]
    public async Task List_FiltersUnspecifiedCustomer()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var ownCustomer = await SeedOrderAsync(marker, customer, $"Другая компания {marker}");
        var fallbackCandidate = await SeedOrderAsync(marker, null, customer);
        var other = await SeedOrderAsync(marker, $"Другой {marker}", null);

        var emptyList = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?search={marker}&customerEmpty=true", JsonOptions);
        emptyList!.Should().ContainSingle(o => o.Id == fallbackCandidate.Id);
        emptyList.Should().NotContain(o => o.Id == ownCustomer.Id || o.Id == other.Id);
    }

    [Fact]
    public async Task List_CustomerFilterCombinesWithExistingFilters()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var active = await SeedOrderAsync(marker, customer, null);
        var completed = await SeedOrderAsync(marker, customer, null, OrderStatus.Completed);
        var otherCompleted = await SeedOrderAsync(
            marker, $"Другой {marker}", null, OrderStatus.Completed, completed.ProductId);

        var list = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?status=Completed&search={marker}" +
            $"&productTypeId={completed.Product!.ProductTypeId}&productId={completed.ProductId}" +
            $"&customer={Uri.EscapeDataString(customer)}", JsonOptions);

        list!.Should().ContainSingle(o => o.Id == completed.Id);
        list.Should().NotContain(o => o.Id == active.Id || o.Id == otherCompleted.Id);
    }

    [Fact]
    public async Task List_UnknownCustomer_ReturnsEmpty()
    {
        var client = await ManagerClientAsync();
        var unknown = $"Несуществующий {Guid.NewGuid():N}";

        var list = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?customer={Uri.EscapeDataString(unknown)}", JsonOptions);

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_CustomerAndCustomerEmpty_ReturnsBadRequest()
    {
        var client = await ManagerClientAsync();

        var response = await client.GetAsync(
            "/api/orders?customer=ООО%20Ромашка&customerEmpty=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [x] **Step 2: Запустить новый класс и увидеть FAIL**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~OrderCustomerFilterTests"
```

Expected: все пять тестов FAIL из-за отсутствия фильтрации и проверки конфликта параметров.

- [x] **Step 3: Реализовать фильтр**

Заменить `GetList` в `OrdersController` на:

```csharp
[HttpGet]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Observer}")]
public async Task<ActionResult<IEnumerable<OrderDto>>> GetList(
    [FromQuery] OrderStatus? status = null,
    [FromQuery] string? search = null,
    [FromQuery] Guid? productTypeId = null,
    [FromQuery] Guid? productId = null,
    [FromQuery] string? customer = null,
    [FromQuery] bool customerEmpty = false)
{
    var customerFilter = CustomerRules.Clean(customer);
    if (customerEmpty && customerFilter != null)
        return BadRequest("Параметры customer и customerEmpty нельзя передавать вместе.");

    var query = _context.Orders.Include(o => o.Product).ThenInclude(p => p!.ProductType).AsQueryable();
    if (status.HasValue)
        query = query.Where(o => o.Status == status.Value);
    if (!string.IsNullOrWhiteSpace(search))
        query = query.Where(o => o.Number.Contains(search));
    if (productTypeId.HasValue)
        query = query.Where(o => o.Product!.ProductTypeId == productTypeId.Value);
    if (productId.HasValue)
        query = query.Where(o => o.ProductId == productId.Value);
    if (customerFilter != null)
        query = query.Where(o => o.Customer == customerFilter);
    else if (customerEmpty)
        query = query.Where(o => o.Customer == null);

    var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
    var agg = await ComputeAggregatesAsync(orders.Select(o => o.Id).ToList());
    var dtos = orders.Select(o => ToDto(o, agg.GetValueOrDefault(o.Id))).ToList();
    return Ok(dtos);
}
```

- [x] **Step 4: Запустить новый класс и существующие order integration-тесты**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Wintime.Control.Tests.Integration.Orders"
```

Expected: все тесты namespace `Orders` PASS, 0 failed.

- [x] **Step 5: Commit**

```powershell
git add Wintime.Control.API/Controllers/OrdersController.cs Wintime.Control.Tests.Integration/Orders/OrderCustomerFilterTests.cs
git commit -m "feat(orders): фильтр списка по заказчику (PZP-28)" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 4: Frontend — общий `CustomerFilterSelect`

**Files:**
- Create: `Wintime-Control-Frontend/src/components/common/CustomerFilterSelect.vue`
- Create: `Wintime-Control-Frontend/src/components/common/__tests__/CustomerFilterSelect.spec.js`

**Interfaces:**
- Consumes: `fetch(search: string) → Promise<string[]>`; во views это существующая `fetchCompanies`.
- Produces:
  - props `customer: String|null`, `customerEmpty: Boolean`, `fetch: Function`;
  - emits `update:customer`, `update:customerEmpty`;
  - использование: `<CustomerFilterSelect v-model:customer="filters.customer" v-model:customer-empty="filters.customerEmpty" :fetch="fetchCompanies" />`;
  - exposed для тестов: `loadSuggestions`, `onRemoteMethod`, `onVisibleChange`, `selectKey`, `options`.

- [x] **Step 1: Создать failing Vitest-тесты**

Создать `CustomerFilterSelect.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import CustomerFilterSelect from '../CustomerFilterSelect.vue'

const mounted = []
function mountFilter(props = {}) {
  const wrapper = mount(CustomerFilterSelect, {
    props: { customer: null, customerEmpty: false, fetch: vi.fn().mockResolvedValue([]), ...props },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
  mounted.push(wrapper)
  return wrapper
}

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  mounted.splice(0).forEach(wrapper => wrapper.unmount())
  document.body.innerHTML = ''
})

describe('CustomerFilterSelect', () => {
  it('загружает подсказки и всегда добавляет пункт «(не указано)»', async () => {
    const fetch = vi.fn().mockResolvedValue(['ООО Ромашка'])
    const wrapper = mountFilter({ fetch })

    await wrapper.vm.loadSuggestions('рома')

    expect(fetch).toHaveBeenCalledWith('рома')
    expect(wrapper.vm.options.map(option => option.label)).toEqual([
      '(не указано)', 'ООО Ромашка'
    ])
  })

  it('при открытии загружает исходный список', async () => {
    const fetch = vi.fn().mockResolvedValue([])
    const wrapper = mountFilter({ fetch })

    await wrapper.vm.onVisibleChange(true)

    expect(fetch).toHaveBeenCalledWith('')
  })

  it('выбор названия эмитит customer и сбрасывает customerEmpty', () => {
    const wrapper = mountFilter()

    wrapper.vm.selectKey('customer:ООО Ромашка')

    expect(wrapper.emitted('update:customer').at(-1)).toEqual(['ООО Ромашка'])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([false])
  })

  it('отличает специальный пункт от заказчика с таким же названием', () => {
    const wrapper = mountFilter()

    wrapper.vm.selectKey('empty')
    expect(wrapper.emitted('update:customer').at(-1)).toEqual([null])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([true])

    wrapper.vm.selectKey('customer:(не указано)')
    expect(wrapper.emitted('update:customer').at(-1)).toEqual(['(не указано)'])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([false])
  })

  it('очистка возвращает состояние «все»', () => {
    const wrapper = mountFilter({ customer: 'ООО Ромашка' })

    wrapper.vm.selectKey('')

    expect(wrapper.emitted('update:customer').at(-1)).toEqual([null])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([false])
  })

  it('сохраняет выбранное название при смене набора подсказок', async () => {
    const fetch = vi.fn().mockResolvedValue(['ООО Другой'])
    const wrapper = mountFilter({ customer: 'ООО Ромашка', fetch })

    await wrapper.vm.loadSuggestions('друг')

    expect(wrapper.vm.options.map(option => option.label)).toContain('ООО Ромашка')
    expect(wrapper.vm.options.map(option => option.label)).toContain('ООО Другой')
  })

  it('дебаунсит поиск и не эмитит свободный текст как заказчика', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn().mockResolvedValue([])
    const wrapper = mountFilter({ fetch })

    wrapper.vm.onRemoteMethod('первый')
    wrapper.vm.onRemoteMethod('последний')
    await vi.advanceTimersByTimeAsync(299)
    expect(fetch).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    await flushPromises()

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch).toHaveBeenCalledWith('последний')
    expect(wrapper.emitted('update:customer')).toBeUndefined()
  })

  it('ошибка подсказок не сбрасывает выбранное значение', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const wrapper = mountFilter({
      customer: 'ООО Ромашка', fetch: vi.fn().mockRejectedValue(new Error('сеть'))
    })

    await wrapper.vm.loadSuggestions('x')

    expect(wrapper.vm.options.map(option => option.label)).toContain('ООО Ромашка')
    expect(wrapper.emitted('update:customer')).toBeUndefined()
    expect(error).toHaveBeenCalledWith('Ошибка загрузки заказчиков')
  })
})
```

- [x] **Step 2: Запустить тест и увидеть FAIL**

Run (в `Wintime-Control-Frontend`):

```powershell
npx vitest run src/components/common/__tests__/CustomerFilterSelect.spec.js
```

Expected: FAIL — `CustomerFilterSelect.vue` не найден.

- [x] **Step 3: Реализовать компонент**

Создать `CustomerFilterSelect.vue`:

```vue
<template>
  <el-select
    :model-value="selection"
    filterable
    remote
    clearable
    :remote-method="onRemoteMethod"
    :loading="loading"
    :placeholder="placeholder"
    class="w-full"
    @update:model-value="selectKey"
    @visible-change="onVisibleChange"
  >
    <el-option
      v-for="option in options"
      :key="option.key"
      :label="option.label"
      :value="option.key"
    />
  </el-select>
</template>

<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import { ElMessage } from 'element-plus'

const EMPTY_KEY = 'empty'
const CUSTOMER_PREFIX = 'customer:'

const props = defineProps({
  customer: { type: String, default: null },
  customerEmpty: { type: Boolean, default: false },
  fetch: { type: Function, required: true },
  placeholder: { type: String, default: 'Все' }
})
const emit = defineEmits(['update:customer', 'update:customerEmpty'])

const suggestions = ref([])
const loading = ref(false)
let timer = null

const selection = computed(() => {
  if (props.customerEmpty) return EMPTY_KEY
  return props.customer ? CUSTOMER_PREFIX + props.customer : ''
})

const options = computed(() => {
  const names = [...new Set(suggestions.value)]
  if (props.customer && !names.includes(props.customer)) names.unshift(props.customer)
  return [
    { key: EMPTY_KEY, label: '(не указано)' },
    ...names.map(name => ({ key: CUSTOMER_PREFIX + name, label: name }))
  ]
})

async function loadSuggestions(search) {
  loading.value = true
  try {
    suggestions.value = await props.fetch(search ?? '') ?? []
  } catch {
    ElMessage.error('Ошибка загрузки заказчиков')
  } finally {
    loading.value = false
  }
}

function onRemoteMethod(search) {
  clearTimeout(timer)
  timer = setTimeout(() => loadSuggestions(search), 300)
}

async function onVisibleChange(visible) {
  if (visible) await loadSuggestions('')
}

function selectKey(value) {
  if (!value) {
    emit('update:customer', null)
    emit('update:customerEmpty', false)
    return
  }
  if (value === EMPTY_KEY) {
    emit('update:customer', null)
    emit('update:customerEmpty', true)
    return
  }
  if (!value.startsWith(CUSTOMER_PREFIX)) return
  emit('update:customer', value.slice(CUSTOMER_PREFIX.length))
  emit('update:customerEmpty', false)
}

onBeforeUnmount(() => clearTimeout(timer))
defineExpose({ loadSuggestions, onRemoteMethod, onVisibleChange, selectKey, options })
</script>
```

- [x] **Step 4: Запустить тест и увидеть PASS**

Run (в `Wintime-Control-Frontend`):

```powershell
npx vitest run src/components/common/__tests__/CustomerFilterSelect.spec.js
```

Expected: 8 tests PASS, 0 failed.

- [x] **Step 5: Commit**

```powershell
git add Wintime-Control-Frontend/src/components/common/CustomerFilterSelect.vue Wintime-Control-Frontend/src/components/common/__tests__/CustomerFilterSelect.spec.js
git commit -m "feat(frontend): общий фильтр заказчика (PZP-28)" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 5: Frontend — подключить фильтр к трём спискам

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue`
- Modify: `Wintime-Control-Frontend/src/views/OrdersView.vue`
- Test: `Wintime-Control-Frontend/src/views/dictionary/__tests__/ProductTypeDictionary.spec.js`
- Test: `Wintime-Control-Frontend/src/views/dictionary/__tests__/MoldDictionary.spec.js`
- Test: `Wintime-Control-Frontend/src/views/__tests__/OrdersView.spec.js`

**Interfaces:**
- Consumes: `CustomerFilterSelect` Task 4; существующие `productTypesApi.suggestCompanies`, `getList(params)` всех API-клиентов.
- Produces: состояние `filters.customer: string|null`, `filters.customerEmpty: boolean`; в request попадает `customer`, только если непустой, и `customerEmpty: true`, только если выбран режим «не указано».

- [x] **Step 1: Добавить failing view-тесты**

Добавить `import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'` в `ProductTypeDictionary.spec.js`, затем добавить тест:

```js
it('фильтр заказчика передаёт customer/customerEmpty и сохраняет остальные фильтры', async () => {
  const wrapper = mountView()
  await flushPromises()
  const listCalls = productTypesApi.getList.mock.calls.length
  await wrapper.findComponent(CustomerFilterSelect).props('fetch')('Рома')
  expect(productTypesApi.suggestCompanies).toHaveBeenCalledWith('Рома')
  expect(productTypesApi.getList).toHaveBeenCalledTimes(listCalls)
  wrapper.vm.filters.search = 'Крышка'
  wrapper.vm.filters.isActive = true

  wrapper.vm.filters.customer = 'ООО Ромашка'
  wrapper.vm.filters.customerEmpty = false
  await wrapper.vm.loadItems()
  expect(productTypesApi.getList).toHaveBeenLastCalledWith({
    isActive: true, search: 'Крышка', customer: 'ООО Ромашка', customerEmpty: undefined
  })

  wrapper.vm.filters.customer = null
  wrapper.vm.filters.customerEmpty = true
  await wrapper.vm.loadItems()
  expect(productTypesApi.getList).toHaveBeenLastCalledWith({
    isActive: true, search: 'Крышка', customer: undefined, customerEmpty: true
  })

  wrapper.vm.filters.customerEmpty = false
  await wrapper.vm.loadItems()
  expect(productTypesApi.getList).toHaveBeenLastCalledWith({
    isActive: true, search: 'Крышка', customer: undefined, customerEmpty: undefined
  })
})
```

Добавить `suggestCompanies: vi.fn(() => Promise.resolve({ data: [] }))` в mock `productTypesApi` файла `MoldDictionary.spec.js` и `import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'`, затем добавить тест:

```js
it('фильтр заказчика передаёт параметры через тип изделия и сохраняет остальные фильтры', async () => {
  const wrapper = mountView()
  await flushPromises()
  const listCalls = moldsApi.getList.mock.calls.length
  await wrapper.findComponent(CustomerFilterSelect).props('fetch')('Рома')
  expect(moldsApi.getList).toHaveBeenCalledTimes(listCalls)
  wrapper.vm.filters.search = 'Форма'
  wrapper.vm.filters.isActive = true

  wrapper.vm.filters.customer = 'ООО Ромашка'
  wrapper.vm.filters.customerEmpty = false
  await wrapper.vm.loadMolds()
  expect(moldsApi.getList).toHaveBeenLastCalledWith({
    isActive: true, search: 'Форма', customer: 'ООО Ромашка', customerEmpty: undefined
  })

  wrapper.vm.filters.customer = null
  wrapper.vm.filters.customerEmpty = true
  await wrapper.vm.loadMolds()
  expect(moldsApi.getList).toHaveBeenLastCalledWith({
    isActive: true, search: 'Форма', customer: undefined, customerEmpty: true
  })

  wrapper.vm.filters.customerEmpty = false
  await wrapper.vm.loadMolds()
  expect(moldsApi.getList).toHaveBeenLastCalledWith({
    isActive: true, search: 'Форма', customer: undefined, customerEmpty: undefined
  })
})
```

Добавить `import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'` в `OrdersView.spec.js`, затем добавить тест:

```js
it('фильтр заказчика передаёт customer/customerEmpty и сохраняет остальные фильтры', async () => {
  const wrapper = mountView([])
  await flushPromises()
  const listCalls = ordersApi.getList.mock.calls.length
  await wrapper.findComponent(CustomerFilterSelect).props('fetch')('Рома')
  expect(ordersApi.getList).toHaveBeenCalledTimes(listCalls)
  wrapper.vm.filters.search = 'ORD'
  wrapper.vm.filters.status = 'Active'

  wrapper.vm.filters.customer = 'ООО Ромашка'
  wrapper.vm.filters.customerEmpty = false
  await wrapper.vm.loadOrders()
  expect(ordersApi.getList).toHaveBeenLastCalledWith({
    status: 'Active', search: 'ORD', customer: 'ООО Ромашка', customerEmpty: undefined
  })

  wrapper.vm.filters.customer = null
  wrapper.vm.filters.customerEmpty = true
  await wrapper.vm.loadOrders()
  expect(ordersApi.getList).toHaveBeenLastCalledWith({
    status: 'Active', search: 'ORD', customer: undefined, customerEmpty: true
  })

  wrapper.vm.filters.customerEmpty = false
  await wrapper.vm.loadOrders()
  expect(ordersApi.getList).toHaveBeenLastCalledWith({
    status: 'Active', search: 'ORD', customer: undefined, customerEmpty: undefined
  })
})
```

- [x] **Step 2: Запустить три view-теста и увидеть FAIL**

Run (в `Wintime-Control-Frontend`):

```powershell
npx vitest run src/views/dictionary/__tests__/ProductTypeDictionary.spec.js src/views/dictionary/__tests__/MoldDictionary.spec.js src/views/__tests__/OrdersView.spec.js
```

Expected: три новых теста FAIL — текущие `loadItems`/`loadMolds`/`loadOrders` не передают новые параметры.

- [x] **Step 3: Подключить компонент и параметры в `ProductTypeDictionary.vue`**

После form-item «Поиск» и перед «Статус» вставить:

```vue
<el-form-item label="Заказчик">
  <CustomerFilterSelect
    v-model:customer="filters.customer"
    v-model:customer-empty="filters.customerEmpty"
    :fetch="fetchCompanies"
    style="width: 260px;"
  />
</el-form-item>
```

Добавить import:

```js
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'
```

Заменить инициализацию filters и параметры загрузки на:

```js
const filters = reactive({ search: '', isActive: null, customer: null, customerEmpty: false })

async function loadItems() {
  loading.value = true
  try {
    const { data } = await productTypesApi.getList({
      isActive: filters.isActive,
      search: filters.search,
      customer: filters.customer || undefined,
      customerEmpty: filters.customerEmpty ? true : undefined
    })
    items.value = data
  } catch {
    ElMessage.error('Ошибка загрузки типов изделий')
  } finally {
    loading.value = false
  }
}
```

Существующую `fetchCompanies` оставить без изменений:

```js
async function fetchCompanies(search) {
  const { data } = await productTypesApi.suggestCompanies(search)
  return data
}
```

- [x] **Step 4: Подключить компонент и параметры в `MoldDictionary.vue`**

Вставить тот же блок `el-form-item` из Step 3 между «Поиск» и «Статус». Добавить:

```js
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'

const filters = reactive({
  search: '',
  isActive: null,
  customer: null,
  customerEmpty: false
})

async function fetchCompanies(search) {
  const { data } = await productTypesApi.suggestCompanies(search)
  return data
}

const loadMolds = async () => {
  loading.value = true
  try {
    const response = await moldsApi.getList({
      isActive: filters.isActive,
      search: filters.search,
      customer: filters.customer || undefined,
      customerEmpty: filters.customerEmpty ? true : undefined
    })
    molds.value = response.data
  } catch (error) {
    ElMessage.error('Ошибка загрузки пресс-форм')
  } finally {
    loading.value = false
  }
}
```

- [x] **Step 5: Подключить компонент и параметры в `OrdersView.vue`**

Вставить тот же блок `el-form-item` из Step 3 между «Поиск» и «Статус». Добавить import и заменить filters/loadOrders:

```js
import CustomerFilterSelect from '@/components/common/CustomerFilterSelect.vue'

const filters = reactive({ status: null, search: '', customer: null, customerEmpty: false })

async function loadOrders() {
  loading.value = true
  try {
    const { data } = await ordersApi.getList({
      status: filters.status,
      search: filters.search,
      customer: filters.customer || undefined,
      customerEmpty: filters.customerEmpty ? true : undefined
    })
    orders.value = data
  } catch (error) {
    ElMessage.error(apiErrorMessage(error, 'Ошибка загрузки заказов'))
  } finally {
    loading.value = false
  }
}
```

Существующую `fetchCompanies` оставить без изменений. `src/api/productTypes.js`, `src/api/molds.js`, `src/api/orders.js` не менять: их `getList(params)` уже принимает эти параметры.

- [x] **Step 6: Запустить компонентный и три view-теста**

Run (в `Wintime-Control-Frontend`):

```powershell
npx vitest run src/components/common/__tests__/CustomerFilterSelect.spec.js src/views/dictionary/__tests__/ProductTypeDictionary.spec.js src/views/dictionary/__tests__/MoldDictionary.spec.js src/views/__tests__/OrdersView.spec.js
```

Expected: все выбранные тесты PASS, 0 failed.

- [x] **Step 7: Commit**

```powershell
git add Wintime-Control-Frontend/src/views/dictionary/ProductTypeDictionary.vue Wintime-Control-Frontend/src/views/dictionary/MoldDictionary.vue Wintime-Control-Frontend/src/views/OrdersView.vue Wintime-Control-Frontend/src/views/dictionary/__tests__/ProductTypeDictionary.spec.js Wintime-Control-Frontend/src/views/dictionary/__tests__/MoldDictionary.spec.js Wintime-Control-Frontend/src/views/__tests__/OrdersView.spec.js
git commit -m "feat(frontend): фильтр заказчика в списках (PZP-28)" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

---

### Task 6: Документация, версия и полная проверка

**Files:**
- Modify: `docs/user-guide/manager.md:281-293,800-805,842-852`
- Modify: `VERSION`
- Review: `docs/superpowers/specs/2026-11-05-pzp-28-customer-filter-design.md`

**Interfaces:**
- Consumes: рабочие API и UI из Tasks 1-5.
- Produces: документированная версия `1.24.0`, готовая к PR после отдельной команды пользователя.

- [x] **Step 1: Обновить руководство менеджера**

В раздел 4.1 после «Поиск» добавить:

```markdown
- **Заказчик** — начните вводить название и выберите одно из ранее введённых значений. Пункт
  **«(не указано)»** показывает заказы, у которых поле заказчика пустое. Очистка фильтра показывает все заказы.
  Учитывается заказчик самого заказа, а не компания типа изделия.
```

В раздел 9.3 после «Поиск» добавить:

```markdown
- **Заказчик** — фильтр по компании типа изделия, связанного с формой. Начните вводить название и выберите
  одну подсказку. **«(не указано)»** показывает формы, у типа которых нет компании, а также старые формы без типа.
```

В раздел 9.4 перед «Тип изделия:» добавить:

```markdown
Список можно отфильтровать по одному **заказчику**: введите фрагмент и выберите ранее введённое название.
Пункт **«(не указано)»** показывает типы без компании; очистка фильтра возвращает полный список.
```

В подписи скриншота 4.1 заменить «фильтры "Поиск" и "Статус"» на «фильтры "Поиск", "Заказчик" и "Статус"».

- [x] **Step 2: Поднять версию**

Записать в `VERSION` одну строку:

```text
1.24.0
```

- [x] **Step 3: Полный backend regression**

Run из корня:

```powershell
dotnet test Wintime.Control.sln
```

Expected: все unit и integration test projects завершены, **0 failed**. Если есть падения, не создавать финальный коммит: исправить причину и повторить полную команду.

- [x] **Step 4: Полный frontend regression и production build**

Run (в `Wintime-Control-Frontend`):

```powershell
npm test
npm run build
```

Expected: Vitest — **0 failed**; Vite build — exit code 0, без ошибок сборки.

- [x] **Step 5: Проверить diff и состав изменений**

Run из корня:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` без вывода; в status только файлы PZP-28 из File Structure и новые plan/spec-файлы, которые пользователь решит включать отдельно. Нет изменений схемы БД, `SuggestInput.vue`, API DTO или package-файлов.

- [x] **Step 6: Commit документации и версии**

```powershell
git add docs/user-guide/manager.md VERSION
git commit -m "chore(release): VERSION 1.24.0 (PZP-28)" -m "Co-Authored-By: Kimi <noreply@moonshot.ai>"
```

PR создавать только после отдельной команды пользователя; при команде использовать `gh pr create` из `feature/pzp-28-customer-filter`.
