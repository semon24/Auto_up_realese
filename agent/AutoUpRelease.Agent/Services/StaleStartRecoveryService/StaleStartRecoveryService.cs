using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.StaleStartRecoveryService;

public sealed class StaleStartRecoveryService
{
    static readonly TimeSpan StaleStartThreshold = TimeSpan.FromMinutes(2);

    readonly AppOptions _options;

    public StaleStartRecoveryService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var deployDir = _options.ProjectDeploymentPath.Trim();
        var stateFileName = _options.StateProjectFileName.Trim();
        if (!Directory.Exists(deployDir))
            return;

        foreach (var stackDir in Directory.GetDirectories(deployDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!File.Exists(stackStateFile))
                continue;

            var candidates = await StackStateStore.GetStaleStartRecoveryCandidatesAsync(
                stackStateFile,
                StaleStartThreshold);

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RecoverSingleAsync(
                    deployDir,
                    stackDir,
                    stackStateFile,
                    candidate,
                    cancellationToken);
            }
        }
    }

    async Task RecoverSingleAsync(
        string deployDir,
        string stackDir,
        string stackStateFile,
        StaleStartRecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployDir, candidate.StackName);
        if (!Directory.Exists(stackDir) || !File.Exists(stackEnvFile))
            return;

        Console.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] stale start recovery: tag={candidate.StackName}, updatedAtUtc={candidate.UpdatedAtUtc:O}, ports={candidate.PortsCount}");

        try
        {
            await DockerCompose.RunUpDetachedWithDiagnosticsAsync(
                stackDir,
                onProgress: servicesState => SaveRecoveryProgressAsync(stackStateFile, candidate.StackName, servicesState),
                progressPollInterval: TimeSpan.FromSeconds(2));

            var waitResult = await DockerCompose.WaitForServicesReadyAsync(
                stackDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                cancellationToken,
                onProgress: servicesState => SaveRecoveryProgressAsync(stackStateFile, candidate.StackName, servicesState));

            if (!waitResult.IsReady)
            {
                var error = waitResult.HasFailure
                    ? waitResult.Reason ?? "Не удалось автоматически восстановить запуск стека"
                    : "Таймаут автоматического восстановления запуска стека";
                await MarkRecoveryFailedAsync(stackStateFile, candidate.StackName, error);
                return;
            }

            var serviceLinks = DeployEnvLinks.TryRead(stackEnvFile, _options.ServiceLinkEnvKeys);
            await StackStateStore.SaveStackServicesStateAsync(stackStateFile, candidate.StackName, waitResult.ServicesState);
            await StackStateStore.SetOperationAsync(
                stackStateFile,
                candidate.StackName,
                operationType: "start",
                operationStatus: "success");
            await StackStateStore.SetServiceLinksAsync(stackStateFile, candidate.StackName, serviceLinks);

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] stale start recovery succeeded: tag={candidate.StackName}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await MarkRecoveryFailedAsync(stackStateFile, candidate.StackName, ex.Message);
        }
    }

    static async Task SaveRecoveryProgressAsync(
        string stackStateFile,
        string stackName,
        Dictionary<string, DockerServiceState> servicesState)
    {
        await StackStateStore.SaveStackServicesStateAsync(stackStateFile, stackName, servicesState);
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            stackName,
            operationType: "start",
            operationStatus: "in_progress");
    }

    static async Task MarkRecoveryFailedAsync(string stackStateFile, string stackName, string error)
    {
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            stackName,
            operationType: "start",
            operationStatus: "failed",
            error: error);
        Console.Error.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] stale start recovery failed: tag={stackName}, error={error}");
    }
}
