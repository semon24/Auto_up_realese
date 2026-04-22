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
        CancellationToken ct)
    {
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return false;
        if (!string.Equals(typeEl.GetString(), TypePing, StringComparison.Ordinal))
            return false;
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получен ping от api");

        if (ws.State != WebSocketState.Open)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] ping получен, но сокет уже не Open: state={ws.State}");
            return true;
        }

        var pong = JsonSerializer.Serialize(new { type = TypePong });
        var pongBytes = Encoding.UTF8.GetBytes(pong);
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] отправляем pong в api");
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(pongBytes), WebSocketMessageType.Text, true, ct);
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] pong успешно отправлен");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] ошибка отправки pong: {ex.GetType().Name}: {ex.Message}; state={ws.State}");
            throw;
        }
        return true;
    }
}
