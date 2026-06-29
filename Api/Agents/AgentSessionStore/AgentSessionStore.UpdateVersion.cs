using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string ClientMethodUpdateVersion = "update_version";

    readonly ConcurrentDictionary<string, PendingUpdateVersion> _updateVersionWaiters = new();

    sealed class PendingUpdateVersion
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<(bool Ok, string? Error, object? Payload)> Tcs { get; init; }
    }

    public async Task<(bool Ok, string? Error, object? Payload)> StartUpdateVersionWithAgentAsync(
        string hostName,
        string stackName,
        string version,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой", null);
        if (string.IsNullOrWhiteSpace(stackName))
            return (false, "Нужен stackName", null);
        if (string.IsNullOrWhiteSpace(version))
            return (false, "Нужна version", null);
        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var connectionId))
            return (false, "Агент не подключён", null);

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<(bool Ok, string? Error, object? Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingUpdateVersion { Id = id, Tcs = tcs };
        if (!_updateVersionWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Команда обновления версии уже выполняется", null);

        try
        {
            if (!_hostsByConnection.ContainsKey(connectionId))
                return (false, "Агент не подключён", null);

            await _agentHubContext.Clients.Client(connectionId).SendAsync(
                ClientMethodUpdateVersion,
                new
                {
                    id = id.ToString("N"),
                    tag = stackName.Trim(),
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
            if (_updateVersionWaiters.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur.Tcs, tcs))
                _updateVersionWaiters.TryRemove(normalizedHostName, out _);
        }
    }

    public void HandleUpdateVersionCompleted(
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
        if (!_updateVersionWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
            return;
        pending.Tcs.TrySetResult((ok, error, payload));
    }
}
