namespace Wintime.Control.Emulator.Config;

public class EmulatorSettings
{
    public MainApiSettings MainApi { get; set; } = new();
    public MqttSettings Mqtt { get; set; } = new();
    public EmulationSettingsSection Emulation { get; set; } = new();
}

public class MainApiSettings
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public AuthSettings Auth { get; set; } = new();
    public int TimeoutSec { get; set; } = 10;
}
/// <summary>
/// Пока не используется.
/// </summary>
public class ClientAuthSettings
{
    public string TokenEndpoint { get; set; } = "/connect/token";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public class AuthSettings
{
    // Тип авторизации: "Login" (пользователь) или "ApiKey" (ключ)
    public string Type { get; set; } = "Login";
    
    // Для Type = "Login"
    public string LoginEndpoint { get; set; } = "/api/auth/login";
    public string RefreshEndpoint { get; set; } = "/api/auth/refresh";
    public string Username { get; set; } = "";  // логин сервисного пользователя
    public string Password { get; set; } = "";  // пароль сервисного пользователя
    
    // Для Type = "ApiKey"
    public string ApiKey { get; set; } = "";
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";
    
    // Общие настройки
    public int TimeoutSec { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    
    public void Validate()
    {
        if (Type == "Login")
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                throw new InvalidOperationException("Username and Password required for Login auth");
        }
        else if (Type == "ApiKey")
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("ApiKey required for ApiKey auth");
        }
    }
}

public class MqttSettings
{
    public string BrokerUrl { get; set; } = "mqtt://localhost:1883";
    public string TopicTemplate { get; set; } = "control/imm/{immId}/telemetry";
    // TODO : Не используется, нужно удалить.
    public string ClientIdPrefix { get; set; } = "imm_emulator";
}

public class EmulationSettingsSection
{
    public int DefaultMessagesPerMinute { get; set; } = 10;
}