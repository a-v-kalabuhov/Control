using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wintime.Control.Core.DTOs.Connector;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConnectorsController : ControllerBase
{
    private readonly ControlDbContext _context;
    private readonly IConfiguration _configuration;

    public ConnectorsController(ControlDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    /// <summary>
    /// Возвращает список ТПА с шаблонами для указанного типа коннектора.
    /// Авторизация по статичному API-ключу в заголовке X-Api-Key.
    /// </summary>
    [HttpGet("{connectorType}/machines")]
    public async Task<ActionResult<IEnumerable<ConnectorMachineDto>>> GetMachines(
        string connectorType,
        [FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        var expectedKey = _configuration["ConnectorApiKey"];
        if (string.IsNullOrEmpty(expectedKey) || apiKey != expectedKey)
            return Unauthorized();

        var machines = await _context.Imms
            .Include(i => i.Template)
            .Where(i => i.IsActive && i.Template.ConnectorType == connectorType)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.ConnectorAlias,
                i.Template.JsonConfig
            })
            .ToListAsync();

        var result = machines.Select(m =>
        {
            JsonObject? config = null;
            try { config = JsonNode.Parse(m.JsonConfig)?.AsObject(); }
            catch { /* невалидный JSON — возвращаем null */ }

            return new ConnectorMachineDto
            {
                ImmId = m.Id,
                ImmName = m.Name,
                ConnectorAlias = m.ConnectorAlias,
                TemplateConfig = config
            };
        });

        return Ok(result);
    }
}
