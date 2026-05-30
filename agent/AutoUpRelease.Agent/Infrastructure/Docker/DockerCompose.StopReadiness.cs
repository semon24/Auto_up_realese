namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    public static async Task<(bool IsStopped, string? Reason, Dictionary<string, DockerServiceState> ServicesState)>
        WaitForServicesStoppedAsync(
            string composeDir,
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken ct,
            Func<Dictionary<string, DockerServiceState>, Task>? onProgress = null,
            IReadOnlyList<string>? composeFiles = null,
            string? stackName = null,
            IReadOnlyDictionary<string, string>? env = null)
    {
        var startedAt = DateTimeOffset.UtcNow;
        Dictionary<string, DockerServiceState> latest = new(StringComparer.Ordinal);

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            ct.ThrowIfCancellationRequested();

            latest = await GetServicesStateAsync(composeDir, composeFiles, stackName, env);
            if (onProgress is not null)
                await onProgress(latest);

            if (latest.Count == 0)
            {
                if (!await IsRunningAsync(composeDir, composeFiles, stackName, env))
                    return (true, null, latest);

                await Task.Delay(pollInterval, ct);
                continue;
            }

            if (latest.Values.All(IsServiceStopped))
                return (true, null, latest);

            await Task.Delay(pollInterval, ct);
        }

        return (false, "Таймаут ожидания остановки сервисов", latest);
    }

    static bool IsServiceStopped(DockerServiceState s) =>
        !string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(s.State, "restarting", StringComparison.OrdinalIgnoreCase);
}
