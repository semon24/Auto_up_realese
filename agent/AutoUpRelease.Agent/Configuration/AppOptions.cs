using Microsoft.Extensions.Configuration;

namespace AutoUpRelease.Agent;

public sealed class AppOptions
{
    public bool IsSingleProjectMode =>
        !IsMultiProjectMode && string.IsNullOrWhiteSpace(CopyFolderForDeployPath);

    public bool IsMultiProjectMode =>
        IsMultiProjectModeName(Mode);

    public bool IsNewSingleProjectMode =>
        !IsMultiProjectMode && !IsSingleProjectMode && IsSingleProjectRoot(ProjectDeploymentPath, StateProjectFileName);

    public bool IsSingleProjectWorkspaceMode =>
        IsSingleProjectMode || IsNewSingleProjectMode;

    public bool NeedsNewSingleProjectInitialization =>
        !IsSingleProjectMode &&
        !HasLocalComposeFile(ProjectDeploymentPath) &&
        IsEmptyOrStateOnlyProjectRoot(ProjectDeploymentPath, StateProjectFileName);

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

    public static bool HasLocalComposeFile(string? projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
            return false;

        var normalizedProjectDir = projectDir.Trim();
        var composeFileNames = new[]
        {
            "compose.yaml",
            "compose.yml",
            "docker-compose.yaml",
            "docker-compose.yml"
        };

        return composeFileNames.Any(fileName => File.Exists(Path.Combine(normalizedProjectDir, fileName)));
    }

    static bool IsMultiProjectModeName(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        return normalized is "multy_project" or "multi_project" or "multy-project" or "multi-project";
    }

    static bool IsSingleProjectRoot(string? projectDir, string? stateFileName)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir.Trim()))
            return false;

        return HasLocalComposeFile(projectDir) || IsEmptyOrStateOnlyProjectRoot(projectDir, stateFileName);
    }

    static bool IsEmptyOrStateOnlyProjectRoot(string? projectDir, string? stateFileName)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir.Trim()))
            return false;

        var normalizedStateFileName = stateFileName?.Trim();
        foreach (var entry in Directory.EnumerateFileSystemEntries(projectDir.Trim()))
        {
            var name = Path.GetFileName(entry);
            if (!string.IsNullOrWhiteSpace(normalizedStateFileName) &&
                string.Equals(name, normalizedStateFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            return false;
        }

        return true;
    }
}
