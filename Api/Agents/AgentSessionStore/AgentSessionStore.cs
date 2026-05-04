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
    readonly AgentsJsonFile _agentsJsonFile;
    readonly IHubContext<AgentTransportHub> _agentTransportHubContext;
    readonly AgentHubPublisher _agentHubPublisher;

    public AgentSessionStore(
        AgentsJsonFile agentsStatusJsonFile,
        IHubContext<AgentTransportHub> agentTransportHubContext,
        AgentHubPublisher agentHubPublisher)
    {
        _agentsJsonFile = agentsStatusJsonFile;
        _agentTransportHubContext = agentTransportHubContext;
        _agentHubPublisher = agentHubPublisher;
    }

    public Task RegisterAgentConnectionAsync(string? hostName, string connectionId, CancellationToken cancellationToken)
    {
        if (Normalize(hostName) is not { } normalizedHostName)
            throw new ArgumentException("hostName is required", nameof(hostName));

        if (_connectionsByHost.TryGetValue(normalizedHostName, out var previousConnectionId))
            _hostsByConnection.TryRemove(previousConnectionId, out _);
        _connectionsByHost[normalizedHostName] = connectionId;
        _hostsByConnection[connectionId] = normalizedHostName;
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] агент подключился: {normalizedHostName}");
        _agentsJsonFile.UpsertOnAgentConnected(normalizedHostName);
        return _agentHubPublisher.PublishAgentUpdatedAsync(normalizedHostName, cancellationToken);
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
        return _agentHubPublisher.PublishAgentUpdatedAsync(normalizedHostName);
    }

    void OnAgentDisconnected(string normalizedHostName)
    {
        if (_passwordVerifyWaiters.TryRemove(normalizedHostName, out var p))
            p.Tcs.TrySetCanceled();
        if (_dockerComposeUpWaiters.TryRemove(normalizedHostName, out var up))
            up.Tcs.TrySetCanceled();
    }

    static string? Normalize(string? hostName)
    {
        var t = hostName?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
