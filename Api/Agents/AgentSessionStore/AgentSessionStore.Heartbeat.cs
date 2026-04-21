using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public const string MessageTypeAppPing = "ping";
    public const string MessageTypeAppPong = "pong";

    static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    static readonly TimeSpan HeartbeatSilenceTimeout = TimeSpan.FromSeconds(30);

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

    static async Task RunHeartbeatSilenceWatchdogAsync(WebSocket ws, HeartbeatSilenceWatch watch, CancellationToken ct)
    {
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                if (watch.IsSilentLongerThan(HeartbeatSilenceTimeout))
                {
                    try
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "heartbeat silence",
                            CancellationToken.None);
                    }
                    catch
                    {
                    }

                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    static async Task RunHeartbeatSenderAsync(WebSocket ws, CancellationToken ct)
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
}
