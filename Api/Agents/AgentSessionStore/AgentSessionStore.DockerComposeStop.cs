using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string ClientMethodDockerComposeStop = "docker_compose_stop";

    readonly ConcurrentDictionary<string, PendingDockerComposeStop> _dockerComposeStopWaiters = new();

    sealed class PendingDockerComposeStop
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<(bool Ok, string? Error, object? Payload)> Tcs { get; init; }
    }

    public async Task<(bool Ok, string? Error, object? Payload)> StartDockerComposeStopWithAgentAsync(
        string hostName,
        string tag,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой", null);
        if (string.IsNullOrWhiteSpace(tag))
            return (false, "Нужен tag", null);
        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var connectionId))
            return (false, "Агент не подключён", null);

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<(bool Ok, string? Error, object? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingDockerComposeStop { Id = id, Tcs = tcs };
        if (!_dockerComposeStopWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Команда остановки уже выполняется", null);

        try
        {
            if (!_hostsByConnection.ContainsKey(connectionId))
                return (false, "Агент не подключён", null);

            await _agentHubContext.Clients.Client(connectionId).SendAsync(
                ClientMethodDockerComposeStop,
                new
                {
                    id = id.ToString("N"),
                    tag = tag.Trim()
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
            if (_dockerComposeStopWaiters.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur.Tcs, tcs))
                _dockerComposeStopWaiters.TryRemove(normalizedHostName, out _);
        }
    }

    public void HandleDockerComposeStopCompleted(
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
        if (!_dockerComposeStopWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
            return;
        pending.Tcs.TrySetResult((ok, error, payload));
    }
}
