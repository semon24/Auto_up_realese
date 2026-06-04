using Microsoft.Extensions.Configuration;

namespace AutoUpRelease.Agent;

public sealed class AppOptions
{
    public bool IsSingleProjectMode =>
        string.IsNullOrWhiteSpace(CopyFolderForDeployPath);

    [ConfigurationKeyName("SERVER_BACKEND_URL")]
    public string ServerBackendUrl { get; set; }

    [ConfigurationKeyName("AGENT_HOST_NAME")]
    public string AgentHostName { get; set; }

    [ConfigurationKeyName("TYPE")]
    public string Type { get; set; } = "";

    [ConfigurationKeyName("MODE")]
    public string Mode { get; set; } = "";

    [ConfigurationKeyName("AGENT_RECONNECT_SECONDS")]
    public string AgentReconnectSeconds { get; set; }

    [ConfigurationKeyName("AGENT_PASSWORD_JSON_PATH")]
    public string AgentPasswordJsonPath { get; set; }

    [ConfigurationKeyName("AGENTS_JSON_FILE_PATH")]
    public string AgentsJsonFilePath { get; set; }

    [ConfigurationKeyName("PROJECT_DEPLOYMENT_PATH")]
    public string ProjectDeploymentPath { get; set; }

    [ConfigurationKeyName("COPY_FOLDER_FOR_DEPLOY_PATH")]
    public string CopyFolderForDeployPath { get; set; }

    [ConfigurationKeyName("STACK_SINGLE_NAME")]
    public string StackSingleName { get; set; } = "";

    [ConfigurationKeyName("STATE_PROJECT_FILE_NAME")]
    public string StateProjectFileName { get; set; }

    [ConfigurationKeyName("IMAGE_ENV_KEY")]
    public string ImageEnvKey { get; set; }

    [ConfigurationKeyName("DATABASE_PASSWORD_ENV_KEYS")]
    public string DatabasePasswordEnvKeys { get; set; } = "";

    [ConfigurationKeyName("HARBOR_REPOSITORY")]
    public string HarborRepository { get; set; }

    [ConfigurationKeyName("REGISTRY_URL")]
    public string RegistryUrl { get; set; }

    [ConfigurationKeyName("REGISTRY_USER")]
    public string RegistryUser { get; set; }

    [ConfigurationKeyName("REGISTRY_PASSWORD")]
    public string RegistryPassword { get; set; }

    [ConfigurationKeyName("ServiceLinkEnvKeys")]
    public ServiceLinkEnvKeys ServiceLinkEnvKeys { get; set; }

    [ConfigurationKeyName("PortAllocation")]
    public PortAllocationOptions PortAllocation { get; set; }
}
