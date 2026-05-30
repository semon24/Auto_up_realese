using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string ClientMethodDockerComposeUp = "docker_compose_up";

    readonly ConcurrentDictionary<string, PendingDockerComposeUp> _dockerComposeUpWaiters = new();

    sealed class PendingDockerComposeUp
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<(bool Ok, string? Error, object? Payload)> Tcs { get; init; }
    }

    public async Task<(bool Ok, string? Error, object? Payload)> StartDockerComposeUpWithAgentAsync(
        string hostName,
        string stackName,
        string version,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой", null);
        if (string.IsNullOrWhiteSpace(version))
            return (false, "Нужен version", null);
        if (string.IsNullOrWhiteSpace(stackName))
            return (false, "Нужен stackName", null);
        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var connectionId))
            return (false, "Агент не подключён", null);

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<(bool Ok, string? Error, object? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingDockerComposeUp { Id = id, Tcs = tcs };
        if (!_dockerComposeUpWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Команда запуска уже выполняется", null);

        try
        {
            if (!_hostsByConnection.ContainsKey(connectionId))
                return (false, "Агент не подключён", null);

            await _agentHubContext.Clients.Client(connectionId).SendAsync(
                ClientMethodDockerComposeUp,
                new
                {
                    id = id.ToString("N"),
                    stackName = stackName.Trim(),
                    version = version.Trim()
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
            if (_dockerComposeUpWaiters.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur.Tcs, tcs))
                _dockerComposeUpWaiters.TryRemove(normalizedHostName, out _);
        }
    }

    public void HandleDockerComposeUpCompleted(
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
        if (!_dockerComposeUpWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
            return;
        pending.Tcs.TrySetResult((ok, error, payload));
    }
}
