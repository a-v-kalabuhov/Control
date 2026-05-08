using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Auth;

[Collection("Integration")]
public class AuthControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public AuthControllerTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    // =========================================================================
    // POST /api/auth/login
    // =========================================================================

    /// <summary>
    /// Правильные логин и пароль должны вернуть 200 с непустым access token.
    /// </summary>
    [Fact]
    public async Task Login_ValidCredentials_Returns200WithAccessToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "Admin123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Неверный пароль для существующего пользователя должен вернуть 401.
    /// </summary>
    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "admin", password = "wrong_password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Несуществующий логин должен вернуть 401, а не 404 — ответ не должен
    /// раскрывать, существует ли пользователь.
    /// </summary>
    [Fact]
    public async Task Login_UnknownLogin_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "no_such_user", password = "password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Деактивированный пользователь (IsActive = false) должен получить 401,
    /// даже если пароль верный — учётная запись заблокирована администратором.
    /// </summary>
    [Fact]
    public async Task Login_DeactivatedUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { login = "test_inactive", password = "Inactive123!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // GET /api/auth/me
    // =========================================================================

    /// <summary>
    /// Запрос без токена должен вернуть 401 — эндпоинт требует аутентификации.
    /// </summary>
    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Запрос с действующим токеном должен вернуть 200 и корректный логин пользователя.
    /// </summary>
    [Fact]
    public async Task GetMe_WithValidToken_Returns200WithLogin()
    {
        var token = await AuthHelper.GetTokenAsync(_client, "admin", "Admin123!");
        AuthHelper.SetBearerToken(_client, token!);

        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        body.GetProperty("login").GetString().Should().Be("admin");
    }
}
