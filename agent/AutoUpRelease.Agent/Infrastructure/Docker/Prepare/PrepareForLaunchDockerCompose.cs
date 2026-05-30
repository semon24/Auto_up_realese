namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    public static async Task PrepareStackResources(
        string StackName,
        string Version,
        string stackDir,
        string envFilePath,
        string imageEnvKey,
        string postgresPasswordEnvKey)
    {
        var updates = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(imageEnvKey) && string.IsNullOrWhiteSpace(EnvFile.ReadTag(envFilePath, imageEnvKey)))
            updates[imageEnvKey] = Version;

        await EnsureDatabaseResourcesAsync(StackName, postgresPasswordEnvKey, updates);
        await EnsureCommonVolumesAsync(StackName);
        await EnsureStackNetworkAsync(StackName);

        if (!string.IsNullOrWhiteSpace(stackDir))
            updates["CONTAINER_DATA_PATH"] = stackDir;

        if (updates.Count > 0)
            await EnvFile.WriteTagsAsync(envFilePath, updates);
    }
}
