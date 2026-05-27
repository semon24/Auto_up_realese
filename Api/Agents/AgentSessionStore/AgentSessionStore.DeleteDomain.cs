using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string ClientMethodDeleteDomains = "delete_domains";

    readonly ConcurrentDictionary<string, PendingDeleteDomain> _deleteDomainWaiters = new();

    sealed class PendingDeleteDomain
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<(bool Ok, string? Error, object? Payload)> Tcs { get; init; }
    }

    public async Task<(bool Ok, string? Error, object? Payload)> StartDeleteDomainWithAgentAsync(
        string hostName,
        string tag,
        string domain,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой", null);
        if (string.IsNullOrWhiteSpace(tag))
            return (false, "Нужен tag", null);
        if (string.IsNullOrWhiteSpace(domain))
            return (false, "Нужен домен", null);
        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var connectionId))
            return (false, "Агент не подключён", null);

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<(bool Ok, string? Error, object? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingDeleteDomain { Id = id, Tcs = tcs };
        if (!_deleteDomainWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Команда удаления домена уже выполняется", null);

        try
        {
            if (!_hostsByConnection.ContainsKey(connectionId))
                return (false, "Агент не подключён", null);

            var normalizedDomain = domain.Trim().ToLowerInvariant();

            await _agentHubContext.Clients.Client(connectionId).SendAsync(
                ClientMethodDeleteDomains,
                new
                {
                    id = id.ToString("N"),
                    tag = tag.Trim(),
                    domain = normalizedDomain
                },
                cancellationToken);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);

            try
            {
                return await tcs.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return (false, "Агент не ответил", null);
            }
        }
        finally
        {
            if (_deleteDomainWaiters.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur.Tcs, tcs))
                _deleteDomainWaiters.TryRemove(normalizedHostName, out _);
        }
    }

    public void HandleDeleteDomainsCompleted(
        string connectionId,
        string id,
        bool ok,
        string? error,
        object? payload)
    {
        if (!_hostsByConnection.TryGetValue(connectionId, out var normalizedHostName))
            return;
        if (!Guid.TryParse(id, out var msgId))
            return;
        if (!_deleteDomainWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
            return;
        pending.Tcs.TrySetResult((ok, error, payload));
    }
}
