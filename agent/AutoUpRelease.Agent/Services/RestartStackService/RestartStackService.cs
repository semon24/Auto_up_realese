using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.RestartStackService;

public sealed class RestartStackService
{
    readonly AppOptions _options;

    public RestartStackService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task<RestartStackResult> ExecuteAsync(
        string? rawTag,
        CancellationToken ct = default)
    {
        var tag = rawTag?.Trim();
        if (string.IsNullOrEmpty(tag))
            return RestartStackResult.Fail("Нужен tag");

        var deployProjectsDir = _options.ProjectDeploymentPath.Trim();
        var stateFileName = _options.StateProjectFileName.Trim();
        var stackDir = StackWorkspaceManager.GetStackDir(deployProjectsDir, tag);
        var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, tag);
        var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

        if (!Directory.Exists(stackDir))
            return RestartStackResult.Fail($"Стек с тегом '{tag}' не найден");

        try
        {
            Console.WriteLine($"[restart-stack] этап=begin tag={tag} dir={stackDir}");
            var composeEnv = await StackStateStore.GetComposeRuntimeEnvAsync(stackStateFile, tag);

            await SetOperationAsync(stackStateFile, tag, "stopping");
            Console.WriteLine("[restart-stack] этап=stop");
            await DockerCompose.RunStopAsync(stackDir, stackName: tag, env: composeEnv);

            Console.WriteLine("[restart-stack] этап=wait_stopped timeout_sec=600 poll_sec=2");
            var stopWaitResult = await DockerCompose.WaitForServicesStoppedAsync(
                stackDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                ct,
                onProgress: servicesState => SaveProgressStateAsync(stackStateFile, tag, "stopping", servicesState),
                stackName: tag,
                env: composeEnv);
            if (!stopWaitResult.IsStopped)
            {
                Console.WriteLine($"[restart-stack] этап=wait_stopped status=failed reason={stopWaitResult.Reason ?? "<unknown>"}");
                return await FailAsync(stackStateFile, tag, stopWaitResult.Reason ?? "Не удалось дождаться остановки сервисов");
            }

            await SetOperationAsync(stackStateFile, tag, "in_progress");

            Console.WriteLine("[restart-stack] этап=up");
            await DockerCompose.RunUpDetachedWithDiagnosticsAsync(
                stackDir,
                env: composeEnv,
                onProgress: servicesState => SaveProgressStateAsync(stackStateFile, tag, "starting", servicesState),
                progressPollInterval: TimeSpan.FromSeconds(2),
                stackName: tag);

            Console.WriteLine("[restart-stack] этап=wait_ready timeout_sec=600 poll_sec=2");
            var waitResult = await DockerCompose.WaitForServicesReadyAsync(
                stackDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                ct,
                onProgress: servicesState => SaveProgressStateAsync(stackStateFile, tag, "starting", servicesState),
                stackName: tag,
                env: composeEnv);
            if (!waitResult.IsReady)
            {
                Console.WriteLine($"[restart-stack] этап=wait_ready status=failed reason={waitResult.Reason ?? "<unknown>"}");
                return await FailAsync(stackStateFile, tag, BuildRestartFailedMessage(waitResult));
            }

            Console.WriteLine("[restart-stack] этап=complete_success");
            await StackStateStore.SaveStackServicesStateAsync(stackStateFile, tag, waitResult.ServicesState);
            await StackStateStore.SetOperationAsync(
                stackStateFile,
                tag,
                operationType: "restart",
                operationStatus: "success");

            await RebuildAgentsStateAsync();

            return RestartStackResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[restart-stack] этап=exception message={ex.Message}");
            return await FailAsync(stackStateFile, tag, ex.Message);
        }
    }

    async Task SaveProgressStateAsync(
        string stackStateFile,
        string tag,
        string status,
        Dictionary<string, DockerServiceState> servicesState)
    {
        await StackStateStore.SaveStackServicesStateAsync(stackStateFile, tag, servicesState);
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            tag,
            operationType: "restart",
            operationStatus: status);
    }

    async Task SetOperationAsync(string stackStateFile, string tag, string status)
    {
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            tag,
            operationType: "restart",
            operationStatus: status);
        await RebuildAgentsStateAsync();
    }

    async Task<RestartStackResult> FailAsync(string stackStateFile, string tag, string error)
    {
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            tag,
            operationType: "restart",
            operationStatus: "failed",
            error: error);
        await RebuildAgentsStateAsync();
        return RestartStackResult.Fail(error);
    }

    Task RebuildAgentsStateAsync() =>
        AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            _options.ProjectDeploymentPath.Trim(),
            _options.StateProjectFileName.Trim(),
            _options.AgentsJsonFilePath.Trim(),
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);

    static string BuildRestartFailedMessage(
        (bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState) waitResult)
    {
        return waitResult.HasFailure
            ? $"Не удалось успешно перезапустить стек: {waitResult.Reason}"
            : "Не удалось успешно перезапустить стек: сервисы не достигли состояния running/exited за отведенное время";
    }
}
