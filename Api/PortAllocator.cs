namespace AutoUpRelease.Api;

public static class PortAllocator
{
    static readonly SemaphoreSlim AllocationLock = new(1, 1);

    public static async Task<IReadOnlyDictionary<string, int>> AllocateAndWriteEnvAsync(
        string deployProjectsDir,
        string stateFileName,
        string stackName,
        string envFilePath,
        IReadOnlyList<string> keys,
        int scanMin,
        int scanMax,
        CancellationToken cancellationToken = default)
    {
        if (scanMin < 1 || scanMax > 65535 || scanMin > scanMax)
            throw new InvalidOperationException(
                $"Некорректный диапазон сканирования портов: {scanMin}-{scanMax}");

        await AllocationLock.WaitAsync(cancellationToken);
        try
        {
            var chosen = new Dictionary<string, int>(StringComparer.Ordinal);
            var usedPorts = new HashSet<int>();
            var dockerUsedHostPorts = await DockerCompose.GetPublishedTcpHostPortsAsync();
            var stateUsedPorts = await CollectPortsFromStateAsync(deployProjectsDir, stateFileName, stackName);

            foreach (var rawKey in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = rawKey?.Trim();
                if (string.IsNullOrEmpty(key)) continue;

                var port = FindFirstAvailable(scanMin, scanMax, usedPorts, dockerUsedHostPorts, stateUsedPorts);
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
        finally
        {
            AllocationLock.Release();
        }
    }

    static int? FindFirstAvailable(
        int min,
        int max,
        HashSet<int> used,
        HashSet<int> dockerUsedHostPorts,
        HashSet<int> stateUsedPorts)
    {
        for (var p = min; p <= max; p++)
        {
            if (used.Contains(p)) continue;
            if (dockerUsedHostPorts.Contains(p)) continue;
            if (stateUsedPorts.Contains(p)) continue;
            if (HostPortProbe.IsTcpPortAvailable(p))
                return p;
        }

        return null;
    }

    static async Task<HashSet<int>> CollectPortsFromStateAsync(string deployProjectsDir, string stateFileName, string currentStackName)
    {
        var used = new HashSet<int>();
        if (!Directory.Exists(deployProjectsDir))
            return used;

        var stackDirs = Directory.GetDirectories(deployProjectsDir);
        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            if (string.Equals(stackName, currentStackName, StringComparison.Ordinal))
                continue;

            var stateFilePath = Path.Combine(stackDir, stateFileName);
            if (!File.Exists(stateFilePath))
                continue;

            var ports = await StackStateStore.GetAllocatedPortsAsync(stateFilePath, stackName);
            foreach (var port in ports.Values)
                used.Add(port);
        }

        return used;
    }
}
