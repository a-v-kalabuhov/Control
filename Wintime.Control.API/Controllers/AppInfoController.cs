using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.AppInfo;

namespace Wintime.Control.API.Controllers;

/// <summary>
/// Публичные сведения о стенде: по ним фронт показывает подпись «демо-версия» и демо-учётки
/// на экране входа. Режим определяется во время работы, а не при сборке — один образ корректен
/// и на демо, и у клиента.
/// </summary>
[ApiController]
[Route("api/app-info")]
[AllowAnonymous]
public class AppInfoController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppInfoController(IConfiguration configuration) => _configuration = configuration;

    /// <summary>Признак демо-режима — тот же флаг DemoMode, что создаёт демо-аккаунты.</summary>
    [HttpGet]
    public ActionResult<AppInfoDto> Get() =>
        Ok(new AppInfoDto { DemoMode = _configuration.GetValue<bool>("DemoMode") });
}
