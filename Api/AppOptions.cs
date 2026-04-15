using Microsoft.Extensions.Configuration;

namespace AutoUpRelease.Api;

public sealed class AppOptions
{
    [ConfigurationKeyName("EnableSwagger")]
    public bool EnableSwagger { get; set; }

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
    public required string DeployProjectsDir { get; init; }
    public required string FolderForCopyDir { get; init; }
    public required string StateFileName { get; init; }
    public required string ImageEnvKey { get; init; }
    public required string PostgresPasswordEnvKey { get; init; }
    public required PortAllocationOptions PortAllocation { get; init; }
    public required string HarborRepository { get; init; }
    public required string RegistryUrl { get; init; }
    public required string RegistryUser { get; init; }
    public required string RegistryPassword { get; init; }
    public required ServiceLinkEnvKeys ServiceLinkEnvKeys { get; init; }
}
