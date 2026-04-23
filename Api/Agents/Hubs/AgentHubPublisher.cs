using AutoUpRelease.Api.Agents.Json;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents.Hubs;

/// <summary>Единая отправка SignalR-событий о состоянии агента для UI.</summary>
public sealed class AgentHubPublisher(
    AgentsJsonFile agentsStatusJsonFile,
    IHubContext<AgentsHub> agentsHubContext)
{
    public async Task PublishAgentUpdatedAsync(string hostName, CancellationToken cancellationToken = default)
    {
        var normalizedHostName = hostName.Trim();
        var snapshot = agentsStatusJsonFile.ReadSnapshot();
        snapshot.TryGetValue(normalizedHostName, out var status);

        await agentsHubContext.Clients.All.SendAsync(
            AgentsHub.EventAgentUpdated,
            new
            {
                hostName = normalizedHostName,
                status
            },
            cancellationToken);
    }
}
