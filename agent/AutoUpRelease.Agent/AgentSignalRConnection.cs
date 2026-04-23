using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent;

/// <summary>Сборка URI и создание SignalR-подключения к API.</summary>
internal static class AgentSignalRConnection
{
    static readonly TimeSpan ClientServerTimeout = TimeSpan.FromMinutes(1);
    static readonly TimeSpan ClientKeepAliveInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Читает <c>SERVER_BACKEND_URL</c> и создаёт подключение к <c>/hubs/agent</c> c query-параметром hostName.
    /// </summary>
    internal static HubConnection Create(string hostName)
    {
        var raw = Environment.GetEnvironmentVariable("SERVER_BACKEND_URL") ?? "";
        var baseUri = new Uri(raw.Trim().TrimEnd('/'));
        var hubUri = BuildHubUri(baseUri, hostName);

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                if (baseUri.Host.Contains("ngrok", StringComparison.OrdinalIgnoreCase))
                    options.Headers["ngrok-skip-browser-warning"] = "true";
            })
            .WithAutomaticReconnect()
            .Build();

        connection.ServerTimeout = ClientServerTimeout;
        connection.KeepAliveInterval = ClientKeepAliveInterval;
        return connection;
    }

    internal static Uri BuildHubUri(Uri apiBase, string host)
    {
        var ub = new UriBuilder(apiBase)
        {
            Path = $"{apiBase.AbsolutePath.TrimEnd('/')}/hubs/agent",
            Query = $"hostName={Uri.EscapeDataString(host)}"
        };
        return ub.Uri;
    }
}
