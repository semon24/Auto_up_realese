namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    public static async Task PollServicesStateUntilCancelledAsync(
        string composeDir,
        TimeSpan pollInterval,
        Func<Dictionary<string, DockerServiceState>, Task>? onProgress = null,
        IReadOnlyList<string>? composeFiles = null,
        CancellationToken ct = default,
        string? stackName = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        while (!ct.IsCancellationRequested)
        {
            var latest = await GetServicesStateAsync(composeDir, composeFiles, stackName, env);
            if (onProgress is not null)
                await onProgress(latest);

            try
            {
                await Task.Delay(pollInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task<(bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState)>
        WaitForServicesReadyAsync(
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
                // Fallback для сред, где `docker compose ps --format json` недоступен
                // или возвращает неожиданный формат: проверяем готовность через
                // сумму running + exited относительно общего числа сервисов.
                if (await IsRunningAsync(composeDir, composeFiles, stackName, env))
                    return (true, false, null, latest);

                await Task.Delay(pollInterval, ct);
                continue;
            }

            if (latest.Values.Any(IsServiceFailureState))
                return (false, true, "Обнаружено ошибочное состояние сервиса (dead/removing/unhealthy)", latest);

            if (latest.Values.All(IsServiceReadyOrCompleted))
                return (true, false, null, latest);

            await Task.Delay(pollInterval, ct);
        }

        return (false, false, "Таймаут ожидания готовности сервисов", latest);
    }

    static bool IsServiceReadyOrCompleted(DockerServiceState s) =>
        string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s.State, "exited", StringComparison.OrdinalIgnoreCase);

    static bool IsServiceFailureState(DockerServiceState s) =>
        string.Equals(s.State, "dead", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s.State, "removing", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s.Health, "unhealthy", StringComparison.OrdinalIgnoreCase);
}
