using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string ClientMethodVerifyPassword = "verify_password";

    readonly ConcurrentDictionary<string, PendingPasswordVerify> _passwordVerifyWaiters = new();

    sealed class PendingPasswordVerify
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<bool> Tcs { get; init; }
    }

    /// <summary>
    /// Находит подключённый SignalR-клиент агента для <paramref name="hostName"/>,
    /// отправляет запрос проверки пароля и ждёт ответа с тем же id.
    /// </summary>
    public async Task<(bool Ok, string? Error)> VerifyPasswordWithAgentAsync(
        string hostName,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой");

        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var connectionId))
            return (false, "Агент не подключён");

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingPasswordVerify { Id = id, Tcs = tcs };
        if (!_passwordVerifyWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Проверка пароля уже выполняется");

        try
        {
            if (!_hostsByConnection.ContainsKey(connectionId))
                return (false, "Агент не подключён");

            await _agentTransportHubContext.Clients.Client(connectionId).SendAsync(
                ClientMethodVerifyPassword,
                new
                {
                    id = id.ToString("N"),
                    password
                },
                cancellationToken);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);

            try
            {
                var ok = await tcs.Task.WaitAsync(linked.Token);
                return ok ? (true, null) : (false, "Неверный пароль");
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                return (false, "Агент не ответил");
            }
        }
        finally
        {
            if (_passwordVerifyWaiters.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur.Tcs, tcs))
                _passwordVerifyWaiters.TryRemove(normalizedHostName, out _);
        }
    }

    public void HandlePasswordVerified(
        string connectionId,
        string id,
        bool ok)
    {
        if (!_hostsByConnection.TryGetValue(connectionId, out var normalizedHostName))
            return;
        if (!Guid.TryParse(id, out var msgId))
            return;
        if (!_passwordVerifyWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
            return;
        pending.Tcs.TrySetResult(ok);
    }
}
