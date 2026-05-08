using System.Net.Http.Json;
using System.Text.Json;

namespace Wintime.Control.Tests.Integration.Infrastructure;

public static class AuthHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Получает JWT access token для указанного пользователя.
    /// Возвращает null, если логин/пароль неверны или аккаунт деактивирован.
    /// </summary>
    public static async Task<string?> GetTokenAsync(HttpClient client, string login, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { login, password });
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("accessToken").GetString();
    }

    /// <summary>
    /// Создаёт HttpClient с заголовком Authorization: Bearer {token}.
    /// </summary>
    public static void SetBearerToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
}
