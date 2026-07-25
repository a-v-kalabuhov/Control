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
