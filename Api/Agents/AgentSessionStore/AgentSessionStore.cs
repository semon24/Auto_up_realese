using System.Collections.Concurrent;
using AutoUpRelease.Api.Agents.Hubs;
using AutoUpRelease.Api.Agents.Json;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

/// <summary>Сессии SignalR-агентов: hostName ↔ connectionId.</summary>
public sealed partial class AgentSessionStore
{
    readonly ConcurrentDictionary<string, string> _connectionsByHost = new();
    readonly ConcurrentDictionary<string, string> _hostsByConnection = new();
    readonly AgentConnectionStatusFile _agentsJsonFile;
    readonly IHubContext<AgentHub> _agentHubContext;
    readonly IHubContext<UiHub> _uiHubContext;

    public AgentSessionStore(
        AgentConnectionStatusFile agentsStatusJsonFile,
        IHubContext<AgentHub> agentHubContext,
        IHubContext<UiHub> uiHubContext)
    {
        _agentsJsonFile = agentsStatusJsonFile;
        _agentHubContext = agentHubContext;
        _uiHubContext = uiHubContext;
    }

    public Task RegisterAgentConnectionAsync(string? hostName, string connectionId, string? ipAddress, CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            throw new ArgumentException("hostName is required", nameof(hostName));

        if (Normalize(ipAddress) is not { } normalizedIpAddress)
            throw new ArgumentException("ipAddress is required", nameof(ipAddress)); 

        if (_connectionsByHost.TryGetValue(normalizedHostName, out var previousConnectionId))
            _hostsByConnection.TryRemove(previousConnectionId, out _);
        _connectionsByHost[normalizedHostName] = connectionId;
        _hostsByConnection[connectionId] = normalizedHostName;
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] агент подключился: {normalizedHostName}");
        _agentsJsonFile.UpdateStatusOnConnect(normalizedHostName, normalizedIpAddress);
        return UiHub.PublishAgentUpdatedAsync(_uiHubContext, _agentsJsonFile, normalizedHostName, cancellationToken);
    }

    public Task UnregisterAgentConnectionAsync(string connectionId)
    {
        if (!_hostsByConnection.TryRemove(connectionId, out var normalizedHostName))
            return Task.CompletedTask;
        if (!_connectionsByHost.TryGetValue(normalizedHostName, out var currentConnectionId))
            return Task.CompletedTask;
        if (!string.Equals(currentConnectionId, connectionId, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] игнорируем stale disconnect: host={normalizedHostName}, disconnected={connectionId}, current={currentConnectionId}");
            return Task.CompletedTask;
        }

        _connectionsByHost.TryRemove(normalizedHostName, out _);
        OnAgentDisconnected(normalizedHostName);
        _agentsJsonFile.OnAgentWebSocketClosed(normalizedHostName);
        return UiHub.PublishAgentUpdatedAsync(_uiHubContext, _agentsJsonFile, normalizedHostName);
    }

    /// <summary>Текущий SignalR-connectionId для агента по имени хоста.</summary>
    public bool TryGetAgentConnectionForHost(string? hostName, out string connectionId)
    {
        connectionId = "";
        if (Normalize(hostName) is not { } normalizedHostName)
            return false;

        return _connectionsByHost.TryGetValue(normalizedHostName, out connectionId!)
            && !string.IsNullOrWhiteSpace(connectionId);
    }

    void OnAgentDisconnected(string normalizedHostName)
    {
        if (_passwordVerifyWaiters.TryRemove(normalizedHostName, out var p))
            p.Tcs.TrySetCanceled();
        if (_dockerComposeUpWaiters.TryRemove(normalizedHostName, out var up))
            up.Tcs.TrySetCanceled();
        if (_dockerComposeDownWaiters.TryRemove(normalizedHostName, out var down))
            down.Tcs.TrySetCanceled();
    }

    static string? Normalize(string? hostName)
    {
        var t = hostName?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
