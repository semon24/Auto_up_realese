using System.Text.Json.Serialization;

namespace AutoUpRelease.Api;

public record StartBody(
    [property: JsonPropertyName("stackName")] string? StackName,
    [property: JsonPropertyName("version")] string? Version);

public record AgentPasswordBody(
    [property: JsonPropertyName("password")] string? Password);

public sealed record AddDomainsBody(
    [property: JsonPropertyName("stackName")] string? StackName,
    [property: JsonPropertyName("domains")] List<string>? Domains);

public sealed record DeleteDomainBody(
    [property: JsonPropertyName("stackName")] string? StackName,
    [property: JsonPropertyName("domain")] string? Domain);

public record TagItem(string Tag);

public sealed record SslCertificateInfo(
    string Domain,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    int? DaysLeft,
    bool IsValid,
    string? Error);
