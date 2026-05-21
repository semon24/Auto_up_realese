using Microsoft.Extensions.Configuration;

namespace AutoUpRelease.Api;

public sealed class AppOptions
{
    [ConfigurationKeyName("EnableSwagger")]
    public bool EnableSwagger { get; set; }

    /// <summary>Абсолютный путь к agents.json. Пусто — не создавать файл при старте.</summary>
    [ConfigurationKeyName("AGENTS_JSON_PATH")]
    public string AgentsJsonPath { get; set; } = "";
}

public sealed class ResolvedAppOptions
{
    public required bool EnableSwagger { get; init; }
    /// <summary>Полный путь к agents.json или null, если AGENTS_JSON_PATH не задан.</summary>
    public string? AgentsJsonPath { get; init; }
}
