using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string ClientMethodAddDomains = "add_domains";

    readonly ConcurrentDictionary<string, PendingAddDomains> _addDomainsWaiters = new();

    sealed class PendingAddDomains
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<(bool Ok, string? Error, object? Payload)> Tcs { get; init; }
    }

    public async Task<(bool Ok, string? Error, object? Payload)> StartAddDomainsWithAgentAsync(
        string hostName,
        string tag,
        List<string> domains,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой", null);
        if (string.IsNullOrWhiteSpace(tag))
            return (false, "Нужен tag", null);
        if (domains is null || !domains.Any(x => !string.IsNullOrWhiteSpace(x)))
            return (false, "Нужен domains", null);
        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var connectionId))
            return (false, "Агент не подключён", null);

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<(bool Ok, string? Error, object? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingAddDomains { Id = id, Tcs = tcs };
        if (!_addDomainsWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Команда добавления агента уже выполняется", null);

        try
        {
            if (!_hostsByConnection.ContainsKey(connectionId))
                return (false, "Агент не подключён", null);

            var normalizedDomains = domains
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            if (normalizedDomains.Count == 0)
                return (false, "Нужен хотя бы один домен", null);

            await _agentHubContext.Clients.Client(connectionId).SendAsync(
                ClientMethodAddDomains,
                new
                {
                    id = id.ToString("N"),
                    tag = tag.Trim(),
                    domains = normalizedDomains
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
            if (_addDomainsWaiters.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur.Tcs, tcs))
                _addDomainsWaiters.TryRemove(normalizedHostName, out _);
        }
    }

    public void HandleAddDomainsCompleted(
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
        if (!_addDomainsWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
            return;
        pending.Tcs.TrySetResult((ok, error, payload));
    }
}
