namespace Wintime.Control.Emulator.Services;

using Refit;
using Wintime.Control.Emulator.Models;


public interface IImmApiClient
{
    [Get("/api/imm")]
    Task<List<ImmDto>> GetImmsAsync([Authorize] CancellationToken ct);

    [Get("/api/templates/{id}")]
    Task<TemplateDto> GetTemplateAsync(string id, [Authorize] CancellationToken ct);
}