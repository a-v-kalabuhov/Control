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
