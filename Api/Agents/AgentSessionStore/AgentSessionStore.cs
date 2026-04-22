using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AutoUpRelease.Api.Agents.Json;

namespace AutoUpRelease.Api.Agents;

/// <summary>WebSocket-сессии агентов: /api/agent/ws?hostName=...</summary>
public sealed partial class AgentSessionStore
{
    readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
    readonly AgentsJsonFile? _agentsJson;

    public AgentSessionStore(AgentsJsonFile agentsJsonFile) =>
        _agentsJson = agentsJsonFile;

    public async Task RunAgentWebSocketAsync(string? hostName, WebSocket webSocket, CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "hostName required", CancellationToken.None);
            return;
        }

        WebSocket? previous = null;
        _sockets.AddOrUpdate(normalizedHostName, webSocket, (_, old) =>
        {
            previous = old;
            return webSocket;
        });

        if (previous is { State: WebSocketState.Open } && !ReferenceEquals(previous, webSocket))
        {
            try
            {
                await previous.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None);
            }
            catch
            {
            }
        }

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] агент подключился: {normalizedHostName}");
        _agentsJson?.UpsertOnAgentConnected(normalizedHostName);

        using var bufferMs = new MemoryStream();
        var scratch = new byte[4096];
        var silenceWatch = new HeartbeatSilenceWatch();
        using var hbCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatSenderAsync(normalizedHostName, webSocket, hbCts.Token);
        var silenceWatchdogTask = RunAgentHeartbeatSilenceWatchdogAsync(webSocket, silenceWatch, hbCts);

        try
        {
            while (webSocket.State == WebSocketState.Open && !hbCts.Token.IsCancellationRequested)
            {
                var text = await ReceiveFullTextAsync(webSocket, bufferMs, scratch, hbCts.Token);
                if (text is null)
                    break;

                TryDispatchIncomingAgentJson(normalizedHostName, text, silenceWatch);
            }
        }
        catch (OperationCanceledException) when (hbCts.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            // удалённая сторона оборвала соединение без close-handshake — считаем обычным disconnect
        }
        finally
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] вошли в finally для агента {normalizedHostName}, state={webSocket.State}");
            hbCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch
            {
            }

            try
            {
                await silenceWatchdogTask;
            }
            catch
            {
            }

            OnAgentSocketClosed(normalizedHostName);

            if (_sockets.TryGetValue(normalizedHostName, out var cur) && ReferenceEquals(cur, webSocket))
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] удаляем сокет агента {normalizedHostName} из _sockets");
                _sockets.TryRemove(normalizedHostName, out _);
                _agentsJson?.OnAgentWebSocketClosed(normalizedHostName);
            }

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] вызываем CloseAsync для агента {normalizedHostName}, state={webSocket.State}");
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                }
            }

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] финальное состояние сокета агента {normalizedHostName}: {webSocket.State}");
        }
    }

    void TryDispatchIncomingAgentJson(
        string normalizedHostName,
        string text,
        HeartbeatSilenceWatch silenceWatch)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (TryHandleHeartbeatIncomingJson(normalizedHostName, root, silenceWatch))
                return;
            TryHandlePasswordIncomingJson(normalizedHostName, root);
        }
        catch
        {
            // некорректный JSON — игнор
        }
    }

    static async Task<string?> ReceiveFullTextAsync(
        WebSocket webSocket,
        MemoryStream bufferMs,
        byte[] scratch,
        CancellationToken cancellationToken)
    {
        bufferMs.SetLength(0);
        var messageComplete = false;
        while (!messageComplete)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(scratch), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType == WebSocketMessageType.Text)
                bufferMs.Write(scratch, 0, result.Count);
            messageComplete = result.EndOfMessage;
        }

        return bufferMs.Length == 0 ? "" : Encoding.UTF8.GetString(bufferMs.GetBuffer(), 0, (int)bufferMs.Length);
    }

    static string? Normalize(string? hostName)
    {
        var t = hostName?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
