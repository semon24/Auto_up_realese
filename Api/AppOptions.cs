using Microsoft.Extensions.Configuration;

namespace AutoUpRelease.Api;

public sealed class AppOptions
{
    [ConfigurationKeyName("EnableSwagger")]
    public bool EnableSwagger { get; set; }

    /// <summary>Абсолютный путь к agents.json. Пусто — не создавать файл при старте.</summary>
    [ConfigurationKeyName("AGENTS_JSON_PATH")]
    public string AgentsJsonPath { get; set; } = "";

    [ConfigurationKeyName("DEPLOY_PROJECTS_DIR")]
    public string DeployProjectsDir { get; set; } = "/opt/vneocheredi_auto_release_up/deploy_auto_up_release";

    [ConfigurationKeyName("COPY_FOLDER_FOR_DEPLOY_PATH")]
    public string CopyFolderForDeployPath { get; set; } = "";

    [ConfigurationKeyName("STATE_FILE")]
    public string StateFile { get; set; } = "status-dockers.json";

    [ConfigurationKeyName("IMAGE_ENV_KEY")]
    public string ImageEnvKey { get; set; } = "IMAGE_TAG";

    [ConfigurationKeyName("POSTGRES_PASSWORD_ENV_KEY")]
    public string PostgresPasswordEnvKey { get; set; } = "POSTGRES_PASSWORD|MASTER_POSTGRES_PASSWORD";

    [ConfigurationKeyName("HARBOR_REPOSITORY")]
    public string HarborRepository { get; set; } = "vneocheredi/admin";

    [ConfigurationKeyName("REGISTRY_URL")]
    public string RegistryUrl { get; set; } = "";

    [ConfigurationKeyName("REGISTRY_USER")]
    public string RegistryUser { get; set; } = "";

    [ConfigurationKeyName("REGISTRY_PASSWORD")]
    public string RegistryPassword { get; set; } = "";

    [ConfigurationKeyName("ServiceLinkEnvKeys")]
    public ServiceLinkEnvKeys ServiceLinkEnvKeys { get; set; } = new();

    [ConfigurationKeyName("PortAllocation")]
    public PortAllocationOptions PortAllocation { get; set; } = new();
}

public sealed class ResolvedAppOptions
{
    public required bool EnableSwagger { get; init; }
    /// <summary>Полный путь к agents.json или null, если AGENTS_JSON_PATH не задан.</summary>
    public string? AgentsJsonPath { get; init; }
}
