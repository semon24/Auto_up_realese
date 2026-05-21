using AutoUpRelease.Api.Agents.Json;

namespace AutoUpRelease.Api.Agents;

public sealed partial class AgentSessionStore
{
    public bool IsAuthorizedAgent(string? hostName)
    {
        var normalizedHostName = Normalize(hostName);
        if (normalizedHostName is null)
            return false;

        var snapshot = _agentsJsonFile.ReadSnapshot();
        if (!snapshot.TryGetValue(normalizedHostName, out var status))
            return false;

        return string.Equals(
            status,
            AgentsJsonFile.StatusPasswordAccepted,
            StringComparison.Ordinal);
    }
}
