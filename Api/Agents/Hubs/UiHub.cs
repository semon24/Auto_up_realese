using System.Net;
using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents;
using AutoUpRelease.Api.Agents.Json;
using AutoUpRelease.Api.Ssl;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents.Hubs;

/// <summary>
/// Единственная точка SignalR для **браузера** (<c>/hubs/ui</c>): invoke-снимки + события <see cref="EventAgentUpdated"/> / <see cref="EventStatusUpdated"/>.
/// Агенты подключаются отдельно к <see cref="AgentHub"/> (<c>/hubs/agent</c>) — смешивать два протокола в одном hub не нужно.
/// Сервер рассылает в UI через <see cref="IHubContext{UiHub}"/> (см. <see cref="PublishAgentUpdatedAsync"/>).
/// </summary>
public sealed class UiHub(
    AgentConnectionStatusFile agentsStatusJsonFile,
    AgentServicesSnapshotStore snapshotStore,
    SslCertificateStore sslCertificateStore,
    SslCertificateRefreshService sslCertificateRefreshService,
    AgentSessionStore agentSessions,
    HarborTagsOrchestrator harborTagsOrchestrator) : Hub
{
    public const string EventAgentUpdated = "agent_updated";
    public const string EventStatusUpdated = "status_updated";
    static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromSeconds(35);

    /// <summary>Текущее состояние агентов для первичной синхронизации клиента.</summary>
    public IReadOnlyDictionary<string, AgentConnectionInfo> GetAgentsSnapshot() => agentsStatusJsonFile.ReadSnapshot();

    /// <summary>Актуальные runtime-снимки стеков по хостам агентов.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RuntimeStackDto>>> GetRuntimeSnapshot()
    {
        try
        {
            await sslCertificateRefreshService.RefreshAllAsync(Context.ConnectionAborted);
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] GetRuntimeSnapshot SSL refresh error: {ex.Message}");
        }

        var byHost = snapshotStore.GetFreshStacksByHost(SnapshotMaxAge);
        var result = new Dictionary<string, IReadOnlyList<RuntimeStackDto>>(StringComparer.Ordinal);
        foreach (var hostEntry in byHost)
        {
            var stacks = new List<RuntimeStackDto>(hostEntry.Value.Count);
            foreach (var stackEntry in hostEntry.Value)
                stacks.Add(ToRuntimeStackDto(hostEntry.Key, stackEntry.Key, stackEntry.Value, sslCertificateStore));
            result[hostEntry.Key] = stacks;
        }

        return result;
    }

    /// <summary>Теги Harbor через конкретного агента (по имени хоста); API сам Harbor не дергает.</summary>
    public async Task<TagItem[]> GetHarborTags(string agentHostName)
    {
        var normalizedHostName = agentHostName?.Trim() ?? "";
        if (normalizedHostName.Length == 0)
            throw new HubException("Не указано имя агента.");

        if (!agentSessions.TryGetAgentConnectionForHost(normalizedHostName, out var connectionId))
            throw new HubException(
                $"Агент «{normalizedHostName}» не подключён по SignalR — теги запросить не к кому.");

        TagsResult result;
        try
        {
            result = await harborTagsOrchestrator.RequestTagsFromAgentAsync(
                connectionId,
                TimeSpan.FromMinutes(2),
                Context.ConnectionAborted);
        }
        catch (OperationCanceledException)
        {
            throw new HubException("Запрос тегов отменён (соединение UI закрыто).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] GetHarborTags unexpected error: host={normalizedHostName}, connectionId={connectionId}, error={ex}");
            throw new HubException($"Ошибка получения тегов через агента: {ex.Message}");
        }

        if (!result.Ok)
            throw new HubException(result.Error ?? "Не удалось получить теги Harbor.");

        return result.Items;
    }

    static RuntimeStackDto ToRuntimeStackDto(
        string hostName,
        string tag,
        StackSnapshot snapshot,
        SslCertificateStore sslCertificateStore)
    {
        var activeDomains = new HashSet<string>(
            (snapshot.ServiceDomains ?? [])
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => domain.Trim().ToLowerInvariant()),
            StringComparer.Ordinal);

        var certificates = sslCertificateStore
            .GetCertificates(hostName, tag)
            .Where(certificate => activeDomains.Contains(certificate.Domain.Trim().ToLowerInvariant()))
            .ToArray();

        return
        new(
            tag,
            snapshot.Running,
            snapshot.Version,
            snapshot.OperationType,
            snapshot.OperationStatus,
            snapshot.OperationError,
            snapshot.ServiceLinks,
            snapshot.ServiceDomains,
            snapshot.Services.ToDictionary(
                kv => kv.Key,
                kv => new RuntimeServiceDto(kv.Value.State, kv.Value.Health),
                StringComparer.Ordinal),
            certificates,
            snapshot.Domain);
    }

    /// <summary>Рассылка <see cref="EventAgentUpdated"/> всем вкладкам UI из любого места приложения.</summary>
    public static Task PublishAgentUpdatedAsync(
        IHubContext<UiHub> uiHubContext,
        AgentConnectionStatusFile agentsJsonFile,
        string hostName,
        CancellationToken cancellationToken = default)
    {
        var normalizedHostName = hostName.Trim();
        var snapshot = agentsJsonFile.ReadSnapshot();
        snapshot.TryGetValue(normalizedHostName, out var info);

        return uiHubContext.Clients.All.SendAsync(
            EventAgentUpdated,
            new
            {
                hostName = normalizedHostName,
                status = info?.Status,
                type = info?.Type,
                ipAddress = info?.IpAddress,
                disconnectedAtUtc = info?.DisconnectedAtUtc
            },
            cancellationToken);
    }
}

public sealed record RuntimeStackDto(
    string StackName,
    bool Running,
    string? Version,
    string? OperationType,
    string? OperationStatus,
    string? OperationError,
    IReadOnlyDictionary<string, string>? ServiceLinks,
    IReadOnlyList<string>? ServiceDomains,
    IReadOnlyDictionary<string, RuntimeServiceDto> Services,
    IReadOnlyList<SslCertificateInfo>? Certificates,
    string? Domain);

public sealed record RuntimeServiceDto(string State, string? Health);
