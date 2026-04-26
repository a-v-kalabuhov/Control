namespace Wintime.Control.Emulator.Middleware;

public class SpaMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _indexPath;

    public SpaMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _indexPath = Path.Combine(env.WebRootPath, "index.html");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Если запрос к API или файлу — пропускаем дальше
        if (context.Request.Path.StartsWithSegments("/api") || 
            context.Request.Path.HasValue && 
            File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", context.Request.Path.Value!)))
        {
            await _next(context);
            return;
        }

        // Все остальные запросы → отдаём index.html (для Vue Router)
        if (File.Exists(_indexPath))
        {
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(_indexPath);
        }
        else
        {
            await _next(context);
        }
    }
}