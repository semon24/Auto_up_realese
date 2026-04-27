namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    static async Task EnsureDatabaseResourcesAsync(
        string tag,
        string postgresPasswordEnvKey,
        IDictionary<string, string> updates)
    {
        var (postgresPasswordKey, masterPostgresPasswordKey) = ParsePasswordKeys(postgresPasswordEnvKey);

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
    }

    static (string PostgresPasswordKey, string MasterPostgresPasswordKey) ParsePasswordKeys(string rawKeys)
    {
        var parts = (rawKeys ?? string.Empty)
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var postgresPasswordKey = parts.Length >= 1 ? parts[0] : "POSTGRES_PASSWORD";
        var masterPostgresPasswordKey = parts.Length >= 2 ? parts[1] : "MASTER_POSTGRES_PASSWORD";
        return (postgresPasswordKey, masterPostgresPasswordKey);
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
