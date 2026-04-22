using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Agent;

/// <summary>Приём полных текстовых сообщений с сервера и маршрутизация в <see cref="AgentWebSocketHeartbeatMessages"/> / <see cref="AgentWebSocketPasswordMessages"/>.</summary>
internal static class AgentWebSocketMessages
{
    internal static async Task RunReceiveLoopAsync(ClientWebSocket ws, CancellationToken stoppingToken)
    {
        using var bufferMs = new MemoryStream();
        var scratch = new byte[4096];
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] запускаем receive loop, state={ws.State}");
        try
        {
            while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] ждём следующее сообщение от api, state={ws.State}");
                var text = await ReceiveFullTextAsync(ws, bufferMs, scratch, stoppingToken);
                if (text is null)
                {
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] receive loop получил close/конец потока");
                    break;
                }
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получили текстовое сообщение длиной {text.Length}");
                await HandleServerTextAsync(ws, text, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] receive loop отменён токеном остановки");
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] WebSocketException в receive loop: {ex.Message}; state={ws.State}");
            throw;
        }
        finally
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] выходим из receive loop, final state={ws.State}");
        }
    }

    static async Task HandleServerTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
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
            if (await AgentWebSocketHeartbeatMessages.TryHandleAsync(ws, root, ct))
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
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] ReceiveAsync: type={result.MessageType}, count={result.Count}, end={result.EndOfMessage}");
            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получен close frame: closeStatus={result.CloseStatus}, description={result.CloseStatusDescription ?? "<null>"}");
                return null;
            }
            if (result.MessageType == WebSocketMessageType.Text)
                bufferMs.Write(scratch, 0, result.Count);
            messageComplete = result.EndOfMessage;
        }

        return bufferMs.Length == 0 ? "" : Encoding.UTF8.GetString(bufferMs.GetBuffer(), 0, (int)bufferMs.Length);
    }
}
