using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents.Hubs;

/// <summary>SignalR-хаб для подключения агентов к API.</summary>
public sealed class AgentTransportHub(AgentSessionStore sessions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var hostName = Context.GetHttpContext()?.Request.Query["hostName"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(hostName))
        {
            const string message = "hostName query parameter is required";
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] {message}, connectionId={Context.ConnectionId}");
            Context.Abort();
            throw new HubException(message);
        }

        await sessions.RegisterAgentConnectionAsync(hostName, Context.ConnectionId, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await sessions.UnregisterAgentConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Подтверждение проверки пароля от агента.</summary>
    public Task PasswordVerified(string id, bool ok)
    {
        sessions.HandlePasswordVerified(Context.ConnectionId, id, ok);
        return Task.CompletedTask;
    }
}
