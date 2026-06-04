using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AutoUpRelease.Agent;

/// <summary>Сборка URI и создание SignalR-подключения к API.</summary>
internal static class AgentSignalRConnection
{
    static readonly TimeSpan ClientServerTimeout = TimeSpan.FromMinutes(3);
    static readonly TimeSpan ClientKeepAliveInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Читает <c>SERVER_BACKEND_URL</c> и создаёт подключение к <c>/hubs/agent</c> c query-параметром hostName.
    /// </summary>
    internal static HubConnection Create(string hostName, string? agentType = null, string? agentMode = null)
    {
        var raw = Environment.GetEnvironmentVariable("SERVER_BACKEND_URL") ?? "";
        var baseUri = new Uri(raw.Trim().TrimEnd('/'));
        var hubUri = BuildHubUri(baseUri, hostName, agentType, agentMode);

        var hubBuilder = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                if (baseUri.Host.Contains("ngrok", StringComparison.OrdinalIgnoreCase))
                    options.Headers["ngrok-skip-browser-warning"] = "true";
            })
            .WithAutomaticReconnect()
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                // Временно отключаем шумные технические ping/transport логи SignalR.
                logging.SetMinimumLevel(LogLevel.Information);
                //logging.AddFilter("Microsoft.AspNetCore.SignalR.Client", LogLevel.Trace);
                //logging.AddFilter("Microsoft.AspNetCore.Http.Connections.Client", LogLevel.Trace);
            });

        var connection = hubBuilder.Build();

        connection.ServerTimeout = ClientServerTimeout;
        connection.KeepAliveInterval = ClientKeepAliveInterval;
        return connection;
    }

    internal static Uri BuildHubUri(Uri apiBase, string host, string? agentType = null, string? agentMode = null)
    {
        var queryParts = new List<string>
        {
            $"hostName={Uri.EscapeDataString(host)}"
        };
        var normalizedType = agentType?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedType))
            queryParts.Add($"type={Uri.EscapeDataString(normalizedType)}");
        var normalizedMode = agentMode?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedMode))
            queryParts.Add($"mode={Uri.EscapeDataString(normalizedMode)}");

        var ub = new UriBuilder(apiBase)
        {
            Path = $"{apiBase.AbsolutePath.TrimEnd('/')}/hubs/agent",
            Query = string.Join("&", queryParts)
        };
        return ub.Uri;
    }
}
