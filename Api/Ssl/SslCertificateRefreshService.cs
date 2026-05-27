using AutoUpRelease.Api.Agents;

namespace AutoUpRelease.Api.Ssl;

public sealed class SslCertificateRefreshService(
    AgentServicesSnapshotStore snapshotStore,
    SslCertificateStore sslCertificateStore,
    SslCertificateProbe sslCertificateProbe)
{
    static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromSeconds(35);

    public async Task<(int CheckedStacks, int CheckedDomains)> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var stacksByHost = snapshotStore.GetFreshStacksByHost(SnapshotMaxAge);
        var checkedStacks = 0;
        var checkedDomains = 0;

        foreach (var hostEntry in stacksByHost)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hostName = hostEntry.Key;
            foreach (var stackEntry in hostEntry.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var refreshedDomains = await RefreshStackAsync(hostName, stackEntry.Key, stackEntry.Value, cancellationToken);
                if (refreshedDomains == 0)
                    continue;

                checkedStacks++;
                checkedDomains += refreshedDomains;
            }
        }

        return (checkedStacks, checkedDomains);
    }

    public async Task<int> RefreshStackAsync(string hostName, string tag, CancellationToken cancellationToken = default)
    {
        var stacksByHost = snapshotStore.GetFreshStacksByHost(SnapshotMaxAge);
        if (!stacksByHost.TryGetValue(hostName.Trim(), out var hostStacks))
            return 0;

        if (!hostStacks.TryGetValue(tag.Trim(), out var stackSnapshot))
            return 0;

        return await RefreshStackAsync(hostName, tag, stackSnapshot, cancellationToken);
    }

    async Task<int> RefreshStackAsync(
        string hostName,
        string tag,
        StackSnapshot stackSnapshot,
        CancellationToken cancellationToken)
    {
        var domains = stackSnapshot.ServiceDomains?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (domains is null || domains.Count == 0)
        {
            sslCertificateStore.Upsert(hostName, tag, Array.Empty<SslCertificateInfo>());
            return 0;
        }

        var certificates = new List<SslCertificateInfo>(domains.Count);
        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            certificates.Add(await sslCertificateProbe.ProbeAsync(domain, cancellationToken));
        }

        sslCertificateStore.Upsert(hostName, tag, certificates);
        return certificates.Count;
    }
}
