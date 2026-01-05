using System.Text.Json.Serialization;

namespace Launcher.Models;

public sealed class LoginRequest
{
    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("password")]
    public required string Password { get; set; }
}