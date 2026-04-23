using AutoUpRelease.Api.Agents.Json;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents.Hubs;

/// <summary>SignalR-хаб для UI-обновлений статусов агентов.</summary>
public sealed class AgentsHub(AgentsJsonFile agentsStatusJsonFile) : Hub
{
    public const string EventAgentUpdated = "agent_updated";

    /// <summary>Текущее состояние агентов для первичной синхронизации клиента.</summary>
    public IReadOnlyDictionary<string, string> GetAgentsSnapshot() => agentsStatusJsonFile.ReadSnapshot();
}
