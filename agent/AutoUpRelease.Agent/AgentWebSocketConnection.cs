using System.Net.WebSockets;

namespace AutoUpRelease.Agent;

/// <summary>Сборка URI, создание <see cref="ClientWebSocket"/> и подключение к API.</summary>
internal static class AgentWebSocketConnection
{
    /// <summary>
    /// Читает <c>SERVER_BACKEND_URL</c>, строит ws/wss URI для <c>/api/agent/ws</c>, создаёт сокет и выполняет <see cref="ClientWebSocket.ConnectAsync"/>.
    /// Вызывающий владеет возвращённым сокетом и обязан его освободить.
    /// </summary>
    internal static async Task<ClientWebSocket> ConnectAsync(string hostName, CancellationToken cancellationToken)
    {
        var raw = Environment.GetEnvironmentVariable("SERVER_BACKEND_URL") ?? "";
        var baseUri = new Uri(raw.Trim().TrimEnd('/'));

        var wsUri = BuildWebSocketUri(baseUri, hostName);

        var ws = new ClientWebSocket();
        if (baseUri.Host.Contains("ngrok", StringComparison.OrdinalIgnoreCase))
            ws.Options.SetRequestHeader("ngrok-skip-browser-warning", "true");

        await ws.ConnectAsync(wsUri, cancellationToken);
        return ws;
    }

    internal static Uri BuildWebSocketUri(Uri apiBase, string host)
    {
        var scheme = string.Equals(apiBase.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var ub = new UriBuilder(apiBase)
        {
            Scheme = scheme,
            Path = $"{apiBase.AbsolutePath.TrimEnd('/')}/api/agent/ws",
            Query = $"hostName={Uri.EscapeDataString(host)}"
        };
        return ub.Uri;
    }
}
