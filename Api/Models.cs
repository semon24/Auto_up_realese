using System.Text.Json.Serialization;

namespace AutoUpRelease.Api;

public record StartBody(
    [property: JsonPropertyName("tag")] string? Tag);

public record AgentPasswordBody(
    [property: JsonPropertyName("password")] string? Password);

public sealed record AddDomainsBody(
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("domains")] List<string>? Domains);

public sealed record DeleteDomainBody(
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("domain")] string? Domain);

public record TagItem(string Tag);

public sealed record SslCertificateInfo(
    string Domain,
    DateTimeOffset? NotBeforeUtc,
    DateTimeOffset? NotAfterUtc,
    int? DaysLeft,
    bool IsValid,
    string? Error);
