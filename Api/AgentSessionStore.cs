using System.Net.WebSockets;

namespace AutoUpRelease.Api;

/// <summary>Один агент: WebSocket на /api/agent/ws?hostName=...</summary>
public sealed class AgentSessionStore
{
    WebSocket? _socket;

    public async Task RunAgentWebSocketAsync(string? hostName, WebSocket webSocket, CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is null)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "hostName required", CancellationToken.None);
            return;
        }

        var previous = _socket;
        _socket = webSocket;

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

        try
        {
            var buffer = new byte[4096];
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_socket, webSocket))
                _socket = null;

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    static string? Normalize(string? hostName)
    {
        var t = hostName?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
