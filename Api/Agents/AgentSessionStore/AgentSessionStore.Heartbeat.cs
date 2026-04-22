using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string MessageTypeAppPing = "ping";
    public const string MessageTypeAppPong = "pong";

    static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    static readonly TimeSpan HeartbeatSilenceTimeout = TimeSpan.FromSeconds(60);

    sealed class HeartbeatSilenceWatch
    {
        readonly object _sync = new();
        DateTime _lastPongUtc = DateTime.UtcNow;

        internal void NotifyPongReceived()
        {
            lock (_sync)
                _lastPongUtc = DateTime.UtcNow;
        }

        internal bool IsSilentLongerThan(TimeSpan maxSilence)
        {
            lock (_sync)
                return DateTime.UtcNow - _lastPongUtc >= maxSilence;
        }
    }

    static async Task RunAgentHeartbeatSilenceWatchdogAsync(
        WebSocket ws,
        HeartbeatSilenceWatch watch,
        CancellationTokenSource receiveLoopCts)
    {
        var ct = receiveLoopCts.Token;
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                if (watch.IsSilentLongerThan(HeartbeatSilenceTimeout))
                {
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] тишина по pong >= {HeartbeatSilenceTimeout.TotalSeconds:F0} c, завершаем цикл чтения");
                    receiveLoopCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    static async Task RunHeartbeatSenderAsync(string normalizedHostName, WebSocket ws, CancellationToken ct)
    {
        var pingBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { type = MessageTypeAppPing }));

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, ct);
                if (ws.State != WebSocketState.Open)
                    break;
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] отправляем ping агенту {normalizedHostName}");
                await ws.SendAsync(
                    new ArraySegment<byte>(pingBytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <returns><see langword="true"/>, если сообщение — pong и дальше разбирать не нужно.</returns>
    static bool TryHandleHeartbeatIncomingJson(
        string normalizedHostName,
        JsonElement root,
        HeartbeatSilenceWatch silenceWatch)
    {
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return false;
        if (!string.Equals(typeEl.GetString(), MessageTypeAppPong, StringComparison.Ordinal))
            return false;
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] получен pong от агента {normalizedHostName}");
        silenceWatch.NotifyPongReceived();
        return true;
    }
}
