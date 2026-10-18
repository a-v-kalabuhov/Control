using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
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

    private static ProductDto ToDto(Product p, ISet<Guid> usedIds) => new()
    {
        Id = p.Id, Article = p.Article, FullArticle = p.FullArticle, Name = p.Name,
        IsActive = p.IsActive, IsUsed = usedIds.Contains(p.Id)
    };

    private static ProductTypeDto ToDto(ProductType t, ISet<Guid> usedIds) => new()
    {
        Id = t.Id, Article = t.Article, Name = t.Name, IsActive = t.IsActive,
        Products = t.Products.OrderBy(p => p.Article).Select(p => ToDto(p, usedIds)).ToList()
    };

    // Изделия из набора, которые есть в заказах или заданиях (одним запросом на каждую таблицу).
    private async Task<HashSet<Guid>> UsedProductIdsAsync(IReadOnlyCollection<Guid> productIds)
    {
        var inOrders = await _context.Orders.Where(o => productIds.Contains(o.ProductId))
            .Select(o => o.ProductId).Distinct().ToListAsync();
        var inTasks = await _context.ShiftTasks.Where(t => productIds.Contains(t.ProductId))
            .Select(t => t.ProductId).Distinct().ToListAsync();
        return inOrders.Concat(inTasks).ToHashSet();
    }

    // ── Единое пространство артикулов (PZP-19, решение 4) ──
    // Артикул типа не совпадает ни с другим артикулом типа, ни с полным артикулом изделия.
    // Изделия самого типа (exceptTypeId) пропускаются: их полные артикулы пересчитываются и проверяются отдельно.
    private async Task<string?> TypeArticleConflictAsync(string article, Guid? exceptTypeId)
    {
        var type = await _context.ProductTypes.FirstOrDefaultAsync(t => t.Article == article && t.Id != exceptTypeId);
        if (type != null)
            return $"Артикул '{article}' уже используется типом изделия «{type.Name}».";
        var product = await _context.Products.Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.FullArticle == article && p.ProductTypeId != exceptTypeId);
        return product == null ? null
            : $"Артикул {article} совпадает с полным артикулом изделия «{product.Name}» (тип {product.ProductType!.Article}).";
    }

    // Полный артикул не совпадает ни с полным артикулом другого изделия, ни с артикулом типа.
    private async Task<string?> FullArticleConflictAsync(string fullArticle, IReadOnlyCollection<Guid> exceptProductIds)
    {
        var product = await _context.Products.Include(p => p.ProductType)
            .FirstOrDefaultAsync(p => p.FullArticle == fullArticle && !exceptProductIds.Contains(p.Id));
        if (product != null)
            return $"Полный артикул {fullArticle} уже есть у изделия «{product.Name}» (тип {product.ProductType!.Article}).";
        var type = await _context.ProductTypes.FirstOrDefaultAsync(t => t.Article == fullArticle);
        return type == null ? null
            : $"Полный артикул {fullArticle} совпадает с артикулом типа изделия «{type.Name}».";
    }

    // Подстраховка от гонки: unique-индекс сработал между проверкой и сохранением.
    private static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    /// <summary>Список типов изделий (фильтры: активность, поиск по артикулу/наименованию типа и изделий).</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<ProductTypeDto>>> GetList(
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        var query = _context.ProductTypes.Include(t => t.Products).AsQueryable();

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Article, pattern)
                || p.Products.Any(x => EF.Functions.ILike(x.FullArticle, pattern) || EF.Functions.ILike(x.Name, pattern)));
        }

        var items = await query.OrderBy(p => p.Article).ToListAsync();
        var usedIds = await UsedProductIdsAsync(items.SelectMany(t => t.Products).Select(p => p.Id).ToList());
        return Ok(items.Select(t => ToDto(t, usedIds)).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<ProductTypeDto>> GetById(Guid id)
    {
        var pt = await _context.ProductTypes.Include(t => t.Products).FirstOrDefaultAsync(t => t.Id == id);
        if (pt == null)
            return NotFound();
        var usedIds = await UsedProductIdsAsync(pt.Products.Select(p => p.Id).ToList());
        return Ok(ToDto(pt, usedIds));
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ProductTypeDto>> Create([FromBody] CreateProductTypeRequestDto request)
    {
        var article = ProductArticle.Normalize(request.Article);
        if (string.IsNullOrWhiteSpace(article))
            return BadRequest("Артикул обязателен.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Наименование обязательно.");
        var conflict = await TypeArticleConflictAsync(article, null)
            ?? await FullArticleConflictAsync(ProductArticle.Compose(article, ProductArticle.DefaultPart), Array.Empty<Guid>());
        if (conflict != null)
            return Conflict(conflict);

        // PZP-19: тип создаётся вместе с изделием по умолчанию (у типа всегда ≥ 1 изделия).
        var pt = new ProductType { Article = article, Name = request.Name.Trim(), IsActive = true };
        pt.Products.Add(new Product
        {
            Article = ProductArticle.DefaultPart,
            Name = ProductArticle.DefaultName,
            FullArticle = ProductArticle.Compose(article, ProductArticle.DefaultPart),
            IsActive = true
        });
        _context.ProductTypes.Add(pt);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (IsUniqueViolation(e)) { return Conflict("Артикул уже используется."); }

        return CreatedAtAction(nameof(GetById), new { id = pt.Id }, ToDto(pt, new HashSet<Guid>()));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductTypeRequestDto request)
    {
        var pt = await _context.ProductTypes.Include(t => t.Products).FirstOrDefaultAsync(t => t.Id == id);
        if (pt == null)
            return NotFound();

        if (request.Article != null)
        {
            var trimmed = ProductArticle.Normalize(request.Article);
            if (string.IsNullOrWhiteSpace(trimmed))
                return BadRequest("Артикул не может быть пустым.");
            if (trimmed != pt.Article)
            {
                var conflict = await TypeArticleConflictAsync(trimmed, id);
                if (conflict != null)
                    return Conflict(conflict);
                // Полные артикулы изделий типа пересчитываются; конфликт — до SaveChanges, ничего не сохранено.
                var ownIds = pt.Products.Select(p => p.Id).ToList();
                foreach (var p in pt.Products)
                {
                    conflict = await FullArticleConflictAsync(ProductArticle.Compose(trimmed, p.Article), ownIds);
                    if (conflict != null)
                        return Conflict(conflict);
                }
                pt.Article = trimmed;
                foreach (var p in pt.Products)
                    p.FullArticle = ProductArticle.Compose(trimmed, p.Article);
            }
        }
        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Наименование не может быть пустым.");
            pt.Name = request.Name.Trim();
        }
        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value && !pt.IsActive && !pt.Products.Any(p => p.IsActive))
                return Conflict("Сначала верните из архива хотя бы одно изделие типа");
            pt.IsActive = request.IsActive.Value;
        }

        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (IsUniqueViolation(e)) { return Conflict("Артикул уже используется."); }
        return NoContent();
    }

    /// <summary>Добавить изделие в тип.</summary>
    [HttpPost("{id:guid}/products")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ProductDto>> AddProduct(Guid id, [FromBody] CreateProductRequestDto request)
    {
        var pt = await _context.ProductTypes.FindAsync(id);
        if (pt == null)
            return NotFound();
        var part = ProductArticle.Normalize(request.Article);
        if (string.IsNullOrWhiteSpace(part))
            return BadRequest("Артикул изделия обязателен.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Наименование обязательно.");

        var full = ProductArticle.Compose(pt.Article, part);
        var conflict = await FullArticleConflictAsync(full, Array.Empty<Guid>());
        if (conflict != null)
            return Conflict(conflict);

        var product = new Product
        {
            ProductTypeId = id, Article = part, Name = request.Name.Trim(), FullArticle = full, IsActive = true
        };
        _context.Products.Add(product);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (IsUniqueViolation(e)) { return Conflict("Артикул уже используется."); }

        return CreatedAtAction(nameof(GetById), new { id }, ToDto(product, new HashSet<Guid>()));
    }

    /// <summary>Изменить изделие (часть артикула, наименование, архивный флаг).</summary>
    [HttpPut("{id:guid}/products/{productId:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdateProduct(Guid id, Guid productId, [FromBody] UpdateProductRequestDto request)
    {
        var pt = await _context.ProductTypes.Include(t => t.Products).FirstOrDefaultAsync(t => t.Id == id);
        var product = pt?.Products.FirstOrDefault(p => p.Id == productId);
        if (pt == null || product == null)
            return NotFound();

        if (request.Article != null)
        {
            var part = ProductArticle.Normalize(request.Article);
            if (string.IsNullOrWhiteSpace(part))
                return BadRequest("Артикул изделия не может быть пустым.");
            var full = ProductArticle.Compose(pt.Article, part);
            if (full != product.FullArticle)
            {
                var conflict = await FullArticleConflictAsync(full, new[] { productId });
                if (conflict != null)
                    return Conflict(conflict);
                product.Article = part;
                product.FullArticle = full;
            }
        }
        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Наименование не может быть пустым.");
            product.Name = request.Name.Trim();
        }
        if (request.IsActive.HasValue)
        {
            if (!request.IsActive.Value && product.IsActive && pt.IsActive
                && !pt.Products.Any(p => p.IsActive && p.Id != productId))
                return Conflict("Это последнее активное изделие типа. Чтобы вывести тип из оборота, переведите в архив сам тип");
            product.IsActive = request.IsActive.Value;
        }

        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (IsUniqueViolation(e)) { return Conflict("Артикул уже используется."); }
        return NoContent();
    }

    /// <summary>
    /// Удалить изделие. Физическое удаление — сознательное исключение (ADR-0020):
    /// у неиспользованного изделия нет истории.
    /// </summary>
    [HttpDelete("{id:guid}/products/{productId:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> DeleteProduct(Guid id, Guid productId)
    {
        var pt = await _context.ProductTypes.Include(t => t.Products).FirstOrDefaultAsync(t => t.Id == id);
        var product = pt?.Products.FirstOrDefault(p => p.Id == productId);
        if (pt == null || product == null)
            return NotFound();

        if ((await UsedProductIdsAsync(new[] { productId })).Count > 0)
            return Conflict("Изделие используется в заказах или заданиях — его можно только отправить в архив");
        if (pt.Products.Count <= 1)
            return Conflict("Нельзя удалить последнее изделие типа.");
        if (product.IsActive && pt.IsActive && !pt.Products.Any(p => p.IsActive && p.Id != productId))
            return Conflict("Это последнее активное изделие типа. Чтобы вывести тип из оборота, переведите в архив сам тип");

        _context.Products.Remove(product);
        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation })
        {
            // Гонка: изделие использовали между проверкой и удалением.
            return Conflict("Изделие используется в заказах или заданиях — его можно только отправить в архив");
        }
        return NoContent();
    }
}
