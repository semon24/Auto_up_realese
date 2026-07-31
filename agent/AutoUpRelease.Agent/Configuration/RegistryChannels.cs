namespace AutoUpRelease.Agent.Configuration;

public enum RegistryChannel
{
    Stage,
    Release
}

public sealed record RegistryChannelSettings(string Registry, string HarborRepository);

public static class RegistryChannels
{
    public const string RegistryEnvKey = "VNEOCHEREDI_REGISTRY";

    static readonly RegistryChannelSettings StageSettings = new(
        "registry.ft-soft.ru/vneocheredi-stage",
        "vneocheredi-stage/admin");

    static readonly RegistryChannelSettings ReleaseSettings = new(
        "registry.ft-soft.ru/vneocheredi",
        "vneocheredi/admin");

    public static bool TryParse(string? value, out RegistryChannel channel)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "stage":
                channel = RegistryChannel.Stage;
                return true;
            case "release":
                channel = RegistryChannel.Release;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    public static bool TryFromRegistry(string? registry, out RegistryChannel channel)
    {
        var normalized = registry?.Trim().TrimEnd('/');
        if (string.Equals(normalized, StageSettings.Registry, StringComparison.OrdinalIgnoreCase))
        {
            channel = RegistryChannel.Stage;
            return true;
        }

        if (string.Equals(normalized, ReleaseSettings.Registry, StringComparison.OrdinalIgnoreCase))
        {
            channel = RegistryChannel.Release;
            return true;
        }

        channel = default;
        return false;
    }

    public static bool TryFromEnvFile(string envFilePath, out RegistryChannel channel) =>
        TryFromRegistry(EnvFile.ReadTag(envFilePath, RegistryEnvKey), out channel);

    public static RegistryChannelSettings GetSettings(RegistryChannel channel) =>
        channel switch
        {
            RegistryChannel.Stage => StageSettings,
            RegistryChannel.Release => ReleaseSettings,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported registry channel")
        };

    public static string ToWireValue(RegistryChannel channel) =>
        channel == RegistryChannel.Stage ? "stage" : "release";
}
