using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Agent;

/// <summary>Приём полных текстовых сообщений с сервера и маршрутизация в <see cref="AgentWebSocketHeartbeatMessages"/> / <see cref="AgentWebSocketPasswordMessages"/>.</summary>
internal static class AgentWebSocketMessages
{
    static readonly TimeSpan HeartbeatSilenceTimeout = TimeSpan.FromSeconds(30);

    internal static async Task RunReceiveLoopAsync(ClientWebSocket ws, CancellationToken stoppingToken)
    {
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watch = new AgentHeartbeatWatch();
        var watchdogTask = RunHeartbeatSilenceWatchdogAsync(ws, watch, loopCts.Token);

        using var bufferMs = new MemoryStream();
        var scratch = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open && !loopCts.Token.IsCancellationRequested)
            {
                var text = await ReceiveFullTextAsync(ws, bufferMs, scratch, loopCts.Token);
                if (text is null)
                    break;
                await HandleServerTextAsync(ws, text, loopCts.Token, watch);
            }
        }
        finally
        {
            loopCts.Cancel();
            try
            {
                await watchdogTask;
            }
            catch
            {
            }
        }
    }

    static async Task RunHeartbeatSilenceWatchdogAsync(
        ClientWebSocket ws,
        AgentHeartbeatWatch watch,
        CancellationToken ct)
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

    static async Task HandleServerTextAsync(ClientWebSocket ws, string text, CancellationToken ct, AgentHeartbeatWatch watch)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (await AgentWebSocketHeartbeatMessages.TryHandleAsync(ws, root, ct, watch))
                return;
            if (await AgentWebSocketPasswordMessages.TryHandleAsync(ws, root, ct))
                return;
        }
    }

    static async Task<string?> ReceiveFullTextAsync(
        ClientWebSocket ws,
        MemoryStream bufferMs,
        byte[] scratch,
        CancellationToken cancellationToken)
    {
        bufferMs.SetLength(0);
        var messageComplete = false;
        while (!messageComplete)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(scratch), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType == WebSocketMessageType.Text)
                bufferMs.Write(scratch, 0, result.Count);
            messageComplete = result.EndOfMessage;
        }

        return bufferMs.Length == 0 ? "" : Encoding.UTF8.GetString(bufferMs.GetBuffer(), 0, (int)bufferMs.Length);
    }
}
