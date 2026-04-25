namespace Wintime.Control.Emulator.Models;

using System.Text.Json.Serialization;

// Запрос на логин
public class LoginRequest
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

// Ответ на логин/рефреш
public class LoginResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";
    
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";
    
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
    
    [JsonPropertyName("user")]
    public UserInfo? User { get; set; }
}

public class UserInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";
    
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
}

// Запрос на рефреш
public class RefreshRequest
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";
    
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";
}