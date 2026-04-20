using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;

namespace AutoUpRelease.Agent;

public sealed class AgentWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = Environment.GetEnvironmentVariable("AGENT_HOST_NAME")?.Trim()
                       ?? Environment.MachineName;

        var reconnectSeconds = 5;
        if (int.TryParse(Environment.GetEnvironmentVariable("AGENT_RECONNECT_SECONDS"), out var sec) && sec >= 1)
            reconnectSeconds = sec;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var raw = Environment.GetEnvironmentVariable("SERVER_BACKEND_URL") ?? "";
                var baseUri = new Uri(raw.Trim().TrimEnd('/'));

                var wsUri = BuildWebSocketUri(baseUri, hostName);

                using var ws = new ClientWebSocket();
                if (baseUri.Host.Contains("ngrok", StringComparison.OrdinalIgnoreCase))
                    ws.Options.SetRequestHeader("ngrok-skip-browser-warning", "true");

                await ws.ConnectAsync(wsUri, stoppingToken);
                Console.WriteLine("[agent] подключено");

                var buffer = new byte[4096];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(buffer, stoppingToken);
                    if (r.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[agent] ошибка: {ex.Message}");
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(reconnectSeconds), stoppingToken);
        }

        Console.WriteLine("[agent] выход");
    }

    static Uri BuildWebSocketUri(Uri apiBase, string host)
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
