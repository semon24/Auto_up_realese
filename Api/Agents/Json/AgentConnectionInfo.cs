namespace AutoUpRelease.Api.Agents.Json;

public sealed class AgentConnectionInfo
{
    public string Status { get; set; } = "";
    public string? Type { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset? DisconnectedAtUtc { get; set; }
}
