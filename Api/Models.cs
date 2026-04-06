using System.Text.Json.Serialization;

namespace AutoUpRelease.Api;

public record StartBody(
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("allocatePorts")] bool? AllocatePorts);

public record BranchItem(string Branch, string Tag);

public record GitHubBranch(string Name);

/// <summary>Список переменных в .env и общий диапазон поиска свободных TCP-портов на хосте.</summary>
public class PortAllocationOptions
{
    public List<string> Keys { get; set; } = new();
    /// <summary>Нижняя граница сканирования (включительно). По умолчанию 1024.</summary>
    public int ScanMin { get; set; } = 1024;
    /// <summary>Верхняя граница сканирования (включительно). По умолчанию 65535.</summary>
    public int ScanMax { get; set; } = 65535;
}
