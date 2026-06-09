using System.Text.Json.Nodes;

namespace Wintime.Control.Core.DTOs.Connector;

public class ConnectorMachineDto
{
    public Guid ImmId { get; set; }
    public string ImmName { get; set; } = string.Empty;
    public string? ConnectorAlias { get; set; }
    public JsonObject? TemplateConfig { get; set; }
}
