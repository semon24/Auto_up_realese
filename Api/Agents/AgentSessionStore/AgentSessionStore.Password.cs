using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string MessageTypeVerifyPassword = "verify_password";
    public const string MessageTypePasswordVerified = "password_verified";

    readonly ConcurrentDictionary<string, PendingPasswordVerify> _passwordVerifyWaiters = new();

    sealed class PendingPasswordVerify
    {
        public required Guid Id { get; init; }
        public required TaskCompletionSource<bool> Tcs { get; init; }
    }

    /// <summary>
    /// Находит открытый WebSocket для <paramref name="hostName"/>, отправляет JSON с паролем и id,
    /// ждёт ответ <see cref="MessageTypePasswordVerified"/> с тем же id.
    /// </summary>
    public async Task<(bool Ok, string? Error)> VerifyPasswordWithAgentAsync(
        string hostName,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            return (false, "hostName пустой");

        if (!_sockets.TryGetValue(normalizedHostName, out var ws) || ws.State != WebSocketState.Open)
            return (false, "Агент не подключён");

        var id = Guid.NewGuid();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingPasswordVerify { Id = id, Tcs = tcs };
        if (!_passwordVerifyWaiters.TryAdd(normalizedHostName, pending))
            return (false, "Проверка пароля уже выполняется");

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = MessageTypeVerifyPassword,
                id = id.ToString("N"),
                password
            });
            var bytes = Encoding.UTF8.GetBytes(payload);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
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

    void OnAgentSocketClosed(string normalizedHostName)
    {
        if (_passwordVerifyWaiters.TryRemove(normalizedHostName, out var p))
            p.Tcs.TrySetCanceled();
    }

    void TryHandleIncomingJson(string normalizedHostName, string text, HeartbeatSilenceWatch silenceWatch)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return;
            if (string.Equals(typeEl.GetString(), MessageTypeAppPong, StringComparison.Ordinal))
            {
                silenceWatch.NotifyPongReceived();
                return;
            }

            if (!string.Equals(typeEl.GetString(), MessageTypePasswordVerified, StringComparison.Ordinal))
                return;
            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return;
            if (!Guid.TryParse(idEl.GetString(), out var msgId))
                return;
            if (!_passwordVerifyWaiters.TryGetValue(normalizedHostName, out var pending) || pending.Id != msgId)
                return;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            pending.Tcs.TrySetResult(ok);
        }
        catch
        {
            // некорректный JSON — игнор
        }
    }
}
