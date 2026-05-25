using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.StopStackService;

public sealed class StopStackService
{
    readonly AppOptions _options;

    public StopStackService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task<StopStackResult> ExecuteAsync(
        string? rawTag,
        CancellationToken ct = default)
    {
        var tag = rawTag?.Trim();
        if (string.IsNullOrEmpty(tag))
            return StopStackResult.Fail("Нужен tag");

        var deployProjectsDir = _options.ProjectDeploymentPath.Trim();
        var stateFileName = _options.StateProjectFileName.Trim();
        var stackDir = StackWorkspaceManager.GetStackDir(deployProjectsDir, tag);
        var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

        if (!Directory.Exists(stackDir))
            return StopStackResult.Fail($"Стек с тегом '{tag}' не найден");

        try
        {
            Console.WriteLine($"[stop-stack] этап=begin tag={tag} dir={stackDir}");

            await SetOperationAsync(stackStateFile, tag, "stopping");
            Console.WriteLine("[stop-stack] этап=stop");
            await DockerCompose.RunStopAsync(stackDir);

            Console.WriteLine("[stop-stack] этап=wait_stopped timeout_sec=600 poll_sec=2");
            var stopWaitResult = await DockerCompose.WaitForServicesStoppedAsync(
                stackDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                ct,
                onProgress: servicesState => SaveProgressStateAsync(stackStateFile, tag, "stopping", servicesState));
            if (!stopWaitResult.IsStopped)
            {
                Console.WriteLine($"[stop-stack] этап=wait_stopped status=failed reason={stopWaitResult.Reason ?? "<unknown>"}");
                return await FailAsync(stackStateFile, tag, stopWaitResult.Reason ?? "Не удалось дождаться остановки сервисов");
            }


            Console.WriteLine("[stop-stack] этап=complete_success");
            await StackStateStore.SaveStackServicesStateAsync(stackStateFile, tag, stopWaitResult.ServicesState);
            await StackStateStore.SetOperationAsync(
                stackStateFile,
                tag,
                operationType: "stop",
                operationStatus: "success");

            await RebuildAgentsStateAsync();

            return StopStackResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[stop-stack] этап=exception message={ex.Message}");
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
            operationType: "stop",
            operationStatus: status);
    }

    async Task SetOperationAsync(string stackStateFile, string tag, string status)
    {
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            tag,
            operationType: "stop",
            operationStatus: status);
        await RebuildAgentsStateAsync();
    }

    async Task<StopStackResult> FailAsync(string stackStateFile, string tag, string error)
    {
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            tag,
            operationType: "stop",
            operationStatus: "failed",
            error: error);
        await RebuildAgentsStateAsync();
        return StopStackResult.Fail(error);
    }

    Task RebuildAgentsStateAsync() =>
        AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            _options.ProjectDeploymentPath.Trim(),
            _options.StateProjectFileName.Trim(),
            _options.AgentsJsonFilePath.Trim(),
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);
}
