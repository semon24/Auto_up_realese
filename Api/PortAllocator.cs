namespace AutoUpRelease.Api;

public static class PortAllocator
{
    public static async Task<IReadOnlyDictionary<string, int>> AllocateAndWriteEnvAsync(
        string envFilePath,
        IReadOnlyList<string> keys,
        int scanMin,
        int scanMax,
        CancellationToken cancellationToken = default)
    {
        if (scanMin < 1 || scanMax > 65535 || scanMin > scanMax)
            throw new InvalidOperationException(
                $"Некорректный диапазон сканирования портов: {scanMin}-{scanMax}");

        var chosen = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedPorts = new HashSet<int>();
        var dockerUsedHostPorts = await DockerCompose.GetPublishedTcpHostPortsAsync();

        foreach (var rawKey in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = rawKey?.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var port = FindFirstAvailable(scanMin, scanMax, usedPorts, dockerUsedHostPorts);
            if (port == null)
                throw new InvalidOperationException(
                    $"Нет свободного TCP-порта для {key} в диапазоне {scanMin}-{scanMax}");

            chosen[key] = port.Value;
            usedPorts.Add(port.Value);
        }

        var asStrings = chosen.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);
        await EnvFile.WriteTagsAsync(envFilePath, asStrings);
        return chosen;
    }

    static int? FindFirstAvailable(int min, int max, HashSet<int> used, HashSet<int> dockerUsedHostPorts)
    {
        for (var p = min; p <= max; p++)
        {
            if (used.Contains(p)) continue;
            if (dockerUsedHostPorts.Contains(p)) continue;
            if (HostPortProbe.IsTcpPortAvailable(p))
                return p;
        }

        return null;
    }
}
