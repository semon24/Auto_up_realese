using System.Text.Json.Serialization;

namespace AutoUpRelease.Api;

public record StartBody(
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("allocatePorts")] bool? AllocatePorts);

public record StopBody(
    [property: JsonPropertyName("tag")] string? Tag);

public record AgentPasswordBody(
    [property: JsonPropertyName("password")] string? Password);

public record TagItem(string Tag);

public class ServiceLinkEnvKeys
{
    public string Admin { get; set; } = "DNS_NAME_BACKOFFICE";
    public string Server { get; set; } = "DNS_NAME_SERVER";
    public string Portal { get; set; } = "DNS_NAME_RDV";
    public string Call { get; set; } = "DNS_NAME_CALL";
}

public class PortAllocationOptions
{
    public List<string> Keys { get; set; } = new();
    public int ScanMin { get; set; } = 1024;
    public int ScanMax { get; set; } = 65535;
}
