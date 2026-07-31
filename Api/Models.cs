using System.Text.Json.Serialization;

namespace AutoUpRelease.Api;

public record StartBody(
    [property: JsonPropertyName("stackName")] string? StackName,
    [property: JsonPropertyName("domain")] string? Domain,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("registryChannel")] string? RegistryChannel);

public record AgentPasswordBody(
    [property: JsonPropertyName("password")] string? Password);

public record TagItem(string Tag);

public sealed record SslCertificateInfo(
    string Domain,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    int? DaysLeft,
    bool IsValid,
    string? Error);
