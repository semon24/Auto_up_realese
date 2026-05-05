using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents.Hubs;

/// <summary>SignalR-хаб для подключения агентов к API.</summary>
public sealed class AgentTransportHub(
    AgentSessionStore sessions,
    AgentServicesSnapshotStore snapshotStore,
    IOptions<HubOptions> hubOptions) : Hub
{
    const string HostNameContextKey = "agent-host-name";
    readonly TimeSpan _clientTimeoutInterval = hubOptions.Value.ClientTimeoutInterval ?? TimeSpan.FromMinutes(3);

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

        Context.Items[HostNameContextKey] = hostName.Trim();
        await sessions.RegisterAgentConnectionAsync(hostName, Context.ConnectionId, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var timeoutSeconds = Math.Round(_clientTimeoutInterval.TotalSeconds, MidpointRounding.AwayFromZero);
        var reason = exception?.Message ?? "без ошибки (штатное закрытие сокета)";
        Console.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] агент отключился: connectionId={Context.ConnectionId}; " +
            $"порог детекта разрыва={timeoutSeconds}с; причина={reason}");
        if (Context.Items.TryGetValue(HostNameContextKey, out var hostNameObj) && hostNameObj is string hostName && hostName.Length > 0)
            snapshotStore.RemoveHost(hostName);
        await sessions.UnregisterAgentConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Подтверждение проверки пароля от агента.</summary>
    public Task PasswordVerified(string id, bool ok)
    {
        sessions.HandlePasswordVerified(Context.ConnectionId, id, ok);
        return Task.CompletedTask;
    }

    /// <summary>Результат выполнения команды docker compose up на агенте.</summary>
    public Task DockerComposeUpCompleted(string id, bool ok, string? error, object? payload)
    {
        sessions.HandleDockerComposeUpCompleted(Context.ConnectionId, id, ok, error, payload);
        return Task.CompletedTask;
    }

    /// <summary>Периодический snapshot сервисов от агента.</summary>
    public Task AgentServicesSnapshot(JsonElement payload)
    {
        if (!Context.Items.TryGetValue(HostNameContextKey, out var hostNameObj) || hostNameObj is not string hostName || hostName.Length == 0)
            return Task.CompletedTask;

        var stacksCount = snapshotStore.Upsert(hostName, payload);
        Console.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] получен snapshot от агента: host={hostName}, stacks={stacksCount}");
        LogAcceptedServiceLinks(hostName, payload);
        return Task.CompletedTask;
    }

    static void LogAcceptedServiceLinks(string hostName, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] serviceLinks не найдены: host={hostName}, payload=not-object");
            return;
        }

        if (!TryGetObjectProperty(payload, "stacks", out var stacksElement) &&
            !TryGetObjectProperty(payload, "stack", out stacksElement))
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] serviceLinks не найдены: host={hostName}, stacks-missing");
            return;
        }

        var printed = false;
        foreach (var stackProp in stacksElement.EnumerateObject())
        {
            if (!TryGetObjectProperty(stackProp.Value, "serviceLinks", out var linksElement))
                continue;

            var links = linksElement.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .Select(p => $"{p.Name}={p.Value.GetString()}")
                .ToList();
            var linksText = links.Count == 0 ? "<empty-object>" : string.Join(", ", links);
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] serviceLinks приняты: host={hostName}, tag={stackProp.Name}, links={linksText}");
            printed = true;
        }

        if (!printed)
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] serviceLinks отсутствуют во всех стеках: host={hostName}");
        }
    }

    static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;
            value = prop.Value;
            return true;
        }

        value = default;
        return false;
    }
}
