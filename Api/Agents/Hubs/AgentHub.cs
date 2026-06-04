using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents.Hubs;

/// <summary>SignalR-хаб для подключения агентов к API.</summary>
public sealed class AgentHub(
    AgentSessionStore sessions,
    AgentServicesSnapshotStore snapshotStore,
    HarborTagsOrchestrator harborTagsOrchestrator,
    IOptions<HubOptions> hubOptions,
    IHubContext<UiHub> uiHubContext) : Hub
{
    const string HostNameContextKey = "agent-host-name";
    readonly TimeSpan _clientTimeoutInterval = hubOptions.Value.ClientTimeoutInterval ?? TimeSpan.FromMinutes(3);

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var hostName = Context.GetHttpContext()?.Request.Query["hostName"].FirstOrDefault();
        var agentType = Context.GetHttpContext()?.Request.Query["type"].FirstOrDefault();
        var agentMode = Context.GetHttpContext()?.Request.Query["mode"].FirstOrDefault();
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(hostName))
        {
            const string message = "hostName query parameter is required";
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] {message}, connectionId={Context.ConnectionId}");
            Context.Abort();
            throw new HubException(message);
        }

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            const string message = "RemoteIpAddress is required";
            Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] {message}, connectionId={Context.ConnectionId}");
            Context.Abort();
            throw new HubException(message);
        }

        Context.Items[HostNameContextKey] = hostName.Trim();
        await sessions.RegisterAgentConnectionAsync(
            hostName,
            Context.ConnectionId,
            ipAddress,
            agentType,
            agentMode,
            Context.ConnectionAborted);

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

    /// <summary>Результат выполнения команды docker compose down на агенте.</summary>
    public Task DockerComposeDownCompleted(string id, bool ok, string? error, object? payload)
    {
        sessions.HandleDockerComposeDownCompleted(Context.ConnectionId, id, ok, error, payload);
        return Task.CompletedTask;
    }

    public Task DockerComposeRestartCompleted(string id, bool ok, string? error, object? payload)
    {
        sessions.HandleDockerComposeRestartCompleted(Context.ConnectionId, id, ok, error, payload);
        return Task.CompletedTask;
    }

    public Task DockerComposeStopCompleted(string id, bool ok, string? error, object? payload)
    {
        sessions.HandleDockerComposeStopCompleted(Context.ConnectionId, id, ok, error, payload);
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Массив тегов через <see cref="JsonElement"/> — входящее тело могло прилететь как массив с полем <c>tag</c> без строгой привязки к типам клиента агента.
    /// </remarks>
    public Task TagsUpdated(string id, bool ok, JsonElement tagsPayload, string? error)
    {
        TagItem[]? tags = ok ? DeserializeTagItems(tagsPayload) : null;
        harborTagsOrchestrator.Complete(id, ok, tags, error);
        return Task.CompletedTask;
    }

    static TagItem[] DeserializeTagItems(JsonElement tagsPayload)
    {
        try
        {
            if (tagsPayload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return Array.Empty<TagItem>();
            return tagsPayload.Deserialize<TagItem[]>(TagItemsJson) ?? Array.Empty<TagItem>();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] TagsUpdated: ошибка парсинга массива тегов — {ex.Message}");
            return Array.Empty<TagItem>();
        }
    }

    static readonly JsonSerializerOptions TagItemsJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Периодический snapshot сервисов от агента.</summary>
    public async Task AgentServicesSnapshot(JsonElement payload)
    {
        if (!Context.Items.TryGetValue(HostNameContextKey, out var hostNameObj) || hostNameObj is not string hostName || hostName.Length == 0)
            return ;
        
        if (!sessions.IsAuthorizedAgent(hostName))
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] snapshot проигнорирован: host={hostName}, reason=password not accepted");
            return;
        }

        var stacksCount = snapshotStore.Upsert(hostName, payload);
        Console.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] получен snapshot от агента: host={hostName}, stacks={stacksCount}");
        LogAcceptedServiceLinks(hostName, payload);

        await uiHubContext.Clients.All.SendAsync(
            UiHub.EventStatusUpdated,
            new
            {
                hostName,
                stacksCount,
                snapshot = payload
            });
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
