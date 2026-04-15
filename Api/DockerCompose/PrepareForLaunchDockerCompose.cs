namespace AutoUpRelease.Api;

public static partial class DockerCompose
{
    public static async Task EnsureVersionedResourcesAsync(
        string tag,
        string stackDir,
        string envFilePath,
        string imageEnvKey,
        string postgresPasswordEnvKey)
    {
        var updates = new Dictionary<string, string>(StringComparer.Ordinal);
        var (postgresPasswordKey, masterPostgresPasswordKey) = ParsePasswordKeys(postgresPasswordEnvKey);

        if (!string.IsNullOrWhiteSpace(imageEnvKey) && string.IsNullOrWhiteSpace(EnvFile.ReadTag(envFilePath, imageEnvKey)))
            updates[imageEnvKey] = tag;

        var postgresVolume = $"postgres_data_auto_release_{tag}";
        if (!await VolumeExistsAsync(postgresVolume))
        {
            await EnsureVolumeExistsAsync(postgresVolume);
            updates[postgresPasswordKey] = GeneratePassword();
        }

        var masterPostgresVolume = $"master_postgres_data_auto_release_{tag}";
        if (!await VolumeExistsAsync(masterPostgresVolume))
        {
            await EnsureVolumeExistsAsync(masterPostgresVolume);
            updates[masterPostgresPasswordKey] = GeneratePassword();
        }

        await EnsureVolumeExistsAsync($"rabbit_data_auto_release_{tag}");
        await EnsureNetworkExistsAsync($"web_auto_release_{tag}");

        if (!string.IsNullOrWhiteSpace(stackDir))
            updates["CONTAINER_DATA_PATH"] = stackDir;

        if (updates.Count > 0)
            await EnvFile.WriteTagsAsync(envFilePath, updates);
    }

    static (string PostgresPasswordKey, string MasterPostgresPasswordKey) ParsePasswordKeys(string rawKeys)
    {
        var parts = (rawKeys ?? string.Empty)
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var postgresPasswordKey = parts.Length >= 1 ? parts[0] : "POSTGRES_PASSWORD";
        var masterPostgresPasswordKey = parts.Length >= 2 ? parts[1] : "MASTER_POSTGRES_PASSWORD";
        return (postgresPasswordKey, masterPostgresPasswordKey);
    }

    public static async Task<bool> VolumeExistsAsync(string volumeName)
    {
        if (string.IsNullOrWhiteSpace(volumeName))
            return false;

        var (_, _, inspectExit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "volume", "inspect", volumeName);

        return inspectExit == 0;
    }

    public static async Task EnsureVolumeExistsAsync(string volumeName)
    {
        if (string.IsNullOrWhiteSpace(volumeName))
            return;

        var (_, _, inspectExit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "volume", "inspect", volumeName);

        if (inspectExit == 0)
            return;

        await RunProcessAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["volume", "create", volumeName]);
    }

    public static async Task EnsureNetworkExistsAsync(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            return;

        var (_, _, inspectExit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "network", "inspect", networkName);

        if (inspectExit == 0)
            return;

        await RunProcessAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["network", "create", networkName]);
    }

    static string GeneratePassword(int length = 24)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];

        for (var i = 0; i < length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];

        return new string(chars);
    }
}
