using System.Collections.Concurrent;

namespace AutoUpRelease.Api.Ssl;

public sealed class SslCertificateStore
{
    readonly ConcurrentDictionary<string, HostCertificatesSnapshot> _byHost = new(StringComparer.Ordinal);

    public void Upsert(
        string hostName,
        string tag,
        IReadOnlyList<SslCertificateInfo> certificates)
    {
        var normalizedHostName = Normalize(hostName);
        var normalizedTag = Normalize(tag);
        if (normalizedHostName is null || normalizedTag is null)
            return;

        var hostSnapshot = _byHost.GetOrAdd(normalizedHostName, _ => new HostCertificatesSnapshot());
        hostSnapshot.Stacks[normalizedTag] = new StackCertificatesSnapshot
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Certificates = certificates.ToList()
        };
    }

    public IReadOnlyList<SslCertificateInfo> GetCertificates(string hostName, string tag)
    {
        var normalizedHostName = Normalize(hostName);
        var normalizedTag = Normalize(tag);
        if (normalizedHostName is null || normalizedTag is null)
            return Array.Empty<SslCertificateInfo>();

        if (!_byHost.TryGetValue(normalizedHostName, out var hostSnapshot))
            return Array.Empty<SslCertificateInfo>();

        if (!hostSnapshot.Stacks.TryGetValue(normalizedTag, out var stackSnapshot))
            return Array.Empty<SslCertificateInfo>();

        return stackSnapshot.Certificates;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SslCertificateInfo>>> GetSnapshot()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<SslCertificateInfo>>>(StringComparer.Ordinal);
        foreach (var hostEntry in _byHost)
        {
            var stacks = new Dictionary<string, IReadOnlyList<SslCertificateInfo>>(StringComparer.Ordinal);
            foreach (var stackEntry in hostEntry.Value.Stacks)
                stacks[stackEntry.Key] = stackEntry.Value.Certificates;

            result[hostEntry.Key] = stacks;
        }

        return result;
    }

    static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    sealed class HostCertificatesSnapshot
    {
        public ConcurrentDictionary<string, StackCertificatesSnapshot> Stacks { get; } = new(StringComparer.Ordinal);
    }

    sealed class StackCertificatesSnapshot
    {
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public List<SslCertificateInfo> Certificates { get; init; } = [];
    }
}
