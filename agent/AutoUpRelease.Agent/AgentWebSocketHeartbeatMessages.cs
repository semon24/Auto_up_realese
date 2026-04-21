using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Agent;

/// <summary>Ответ на прикладной heartbeat API (сервер шлёт ping → агент pong).</summary>
internal static class AgentWebSocketHeartbeatMessages
{
    public const string TypePing = "ping";
    public const string TypePong = "pong";

    internal static async Task<bool> TryHandleAsync(
        ClientWebSocket ws,
        JsonElement root,
        CancellationToken ct,
        AgentHeartbeatWatch watch)
    {
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return false;
        if (!string.Equals(typeEl.GetString(), TypePing, StringComparison.Ordinal))
            return false;

        watch.NotifyServerPingReceived();

        var pong = JsonSerializer.Serialize(new { type = TypePong });
        var pongBytes = Encoding.UTF8.GetBytes(pong);
        await ws.SendAsync(new ArraySegment<byte>(pongBytes), WebSocketMessageType.Text, true, ct);
        return true;
    }
}
