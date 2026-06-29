using SingleProjectStateSyncServiceType = AutoUpRelease.Agent.Services.SingleProjectStateSyncService.SingleProjectStateSyncService;
using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.SingleProjectControlService;

    public sealed class SingleProjectControlService
    {
        private readonly AppOptions _options;
        private readonly SingleProjectStateSyncServiceType _singleProjectStateSyncService;

    public SingleProjectControlService(
        IOptions<AppOptions> options,
        SingleProjectStateSyncServiceType singleProjectStateSyncService)
    {
        _options = options.Value;
        _singleProjectStateSyncService = singleProjectStateSyncService;
    }

    public async Task<SingleProjectControlResult> StartAsync(CancellationToken ct = default)
    {
        if (!_options.IsSingleProjectWorkspaceMode)
            return SingleProjectControlResult.Fail("Single-project режим не включён");

        SingleProjectControlContext? context = null;
        try
        {
            context = await BuildContextAsync(ct);

            await StackStateStore.SetOperationAsync(
                context.StackStateFile,
                context.StackName,
                operationType: "start",
                operationStatus: "starting");

            await DockerCompose.LoginAsync(
                _options.RegistryUrl,
                _options.RegistryUser,
                _options.RegistryPassword);

            await DockerCompose.RunAsync(context.ProjectDir, context.StackName, null, "up", "-d");

            var waitResult = await DockerCompose.WaitForServicesReadyAsync(
                context.ProjectDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                ct,
                onProgress: servicesState => SaveProgressStateAsync(
                    context.StackStateFile,
                    context.StackName,
                    "start",
                    "starting",
                    servicesState),
                stackName: context.StackName);

            if (!waitResult.IsReady)
            {
                await TryComposeDownAfterStartFailureAsync(context);
                await StackStateStore.SetOperationAsync(
                    context.StackStateFile,
                    context.StackName,
                    operationType: "start",
                    operationStatus: "failed",
                    error: waitResult.Reason ?? "Не удалось дождаться запуска single-project");
                await _singleProjectStateSyncService.SyncAsync(ct);
                return SingleProjectControlResult.Fail(waitResult.Reason ?? "Не удалось дождаться запуска single-project");
            }

            await StackStateStore.SaveStackServicesStateAsync(
                context.StackStateFile,
                context.StackName,
                waitResult.ServicesState);
            await StackStateStore.SetOperationAsync(
                context.StackStateFile,
                context.StackName,
                operationType: "start",
                operationStatus: "success");
            await _singleProjectStateSyncService.SyncAsync(ct);

            return SingleProjectControlResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                await TryComposeDownAfterStartFailureAsync(context);
                await StackStateStore.SetOperationAsync(
                    context.StackStateFile,
                    context.StackName,
                    operationType: "start",
                    operationStatus: "failed",
                    error: ex.Message);
                await _singleProjectStateSyncService.SyncAsync(ct);
            }

            return SingleProjectControlResult.Fail(ex.Message);
        }
    }

    public async Task<SingleProjectControlResult> StopAsync(CancellationToken ct = default)
    {
        if (!_options.IsSingleProjectWorkspaceMode)
            return SingleProjectControlResult.Fail("Single-project режим не включён");

        SingleProjectControlContext? context = null;
        try
        {
            context = await BuildContextAsync(ct);

            await StackStateStore.SetOperationAsync(
                context.StackStateFile,
                context.StackName,
                operationType: "stop",
                operationStatus: "stopping");

            await DockerCompose.RunStopAsync(context.ProjectDir, stackName: context.StackName);

            var waitResult = await DockerCompose.WaitForServicesStoppedAsync(
                context.ProjectDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                ct,
                onProgress: servicesState => SaveProgressStateAsync(
                    context.StackStateFile,
                    context.StackName,
                    "stop",
                    "stopping",
                    servicesState),
                stackName: context.StackName);

            if (!waitResult.IsStopped)
            {
                await StackStateStore.SetOperationAsync(
                    context.StackStateFile,
                    context.StackName,
                    operationType: "stop",
                    operationStatus: "failed",
                    error: waitResult.Reason ?? "Не удалось дождаться остановки single-project");
                await _singleProjectStateSyncService.SyncAsync(ct);
                return SingleProjectControlResult.Fail(waitResult.Reason ?? "Не удалось дождаться остановки single-project");
            }

            await StackStateStore.SaveStackServicesStateAsync(
                context.StackStateFile,
                context.StackName,
                waitResult.ServicesState);
            await StackStateStore.SetOperationAsync(
                context.StackStateFile,
                context.StackName,
                operationType: "stop",
                operationStatus: "success");
            await _singleProjectStateSyncService.SyncAsync(ct);

            return SingleProjectControlResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                await StackStateStore.SetOperationAsync(
                    context.StackStateFile,
                    context.StackName,
                    operationType: "stop",
                    operationStatus: "failed",
                    error: ex.Message);
                await _singleProjectStateSyncService.SyncAsync(ct);
            }

            return SingleProjectControlResult.Fail(ex.Message);
        }
    }

    public async Task<SingleProjectControlResult> RestartAsync(CancellationToken ct = default)
    {
        if (!_options.IsSingleProjectWorkspaceMode)
            return SingleProjectControlResult.Fail("Single-project режим не включён");

        SingleProjectControlContext? context = null;
        try
        {
            context = await BuildContextAsync(ct);

            await StackStateStore.SetOperationAsync(
                context.StackStateFile,
                context.StackName,
                operationType: "restart",
                operationStatus: "starting");

            await DockerCompose.RunAsync(context.ProjectDir, context.StackName, null, "restart");

            var waitResult = await DockerCompose.WaitForServicesReadyAsync(
                context.ProjectDir,
                timeout: TimeSpan.FromSeconds(600),
                pollInterval: TimeSpan.FromSeconds(2),
                ct,
                onProgress: servicesState => SaveProgressStateAsync(
                    context.StackStateFile,
                    context.StackName,
                    "restart",
                    "starting",
                    servicesState),
                stackName: context.StackName);

            if (!waitResult.IsReady)
            {
                await StackStateStore.SetOperationAsync(
                    context.StackStateFile,
                    context.StackName,
                    operationType: "restart",
                    operationStatus: "failed",
                    error: waitResult.Reason ?? "Не удалось дождаться рестарта single-project");
                await _singleProjectStateSyncService.SyncAsync(ct);
                return SingleProjectControlResult.Fail(waitResult.Reason ?? "Не удалось дождаться рестарта single-project");
            }

            await StackStateStore.SaveStackServicesStateAsync(
                context.StackStateFile,
                context.StackName,
                waitResult.ServicesState);
            await StackStateStore.SetOperationAsync(
                context.StackStateFile,
                context.StackName,
                operationType: "restart",
                operationStatus: "success");
            await _singleProjectStateSyncService.SyncAsync(ct);

            return SingleProjectControlResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                await StackStateStore.SetOperationAsync(
                    context.StackStateFile,
                    context.StackName,
                    operationType: "restart",
                    operationStatus: "failed",
                    error: ex.Message);
                await _singleProjectStateSyncService.SyncAsync(ct);
            }

            return SingleProjectControlResult.Fail(ex.Message);
        }
    }

    async Task<SingleProjectControlContext> BuildContextAsync(CancellationToken ct)
    {
        var projectDir = _options.ProjectDeploymentPath.Trim();
        var stackStateFile = Path.Combine(projectDir, _options.StateProjectFileName.Trim());

        await _singleProjectStateSyncService.SyncAsync(ct);

        var stackNames = await StackStateStore.GetStackNamesAsync(stackStateFile);
        var stackName = stackNames.FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(stackName))
            throw new InvalidOperationException("Не удалось определить имя single-project в state");

        return new SingleProjectControlContext(projectDir, stackStateFile, stackName);
    }

    static async Task TryComposeDownAfterStartFailureAsync(SingleProjectControlContext context)
    {
        try
        {
            await DockerCompose.RunAsync(
                context.ProjectDir,
                context.StackName,
                null,
                "down",
                "-v",
                "--remove-orphans");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[single-project] cleanup after failed start skipped: stackName={context.StackName}, error={ex.Message}");
        }
    }

    static async Task SaveProgressStateAsync(
        string stackStateFile,
        string stackName,
        string operationType,
        string operationStatus,
        Dictionary<string, DockerServiceState> servicesState)
    {
        await StackStateStore.SaveStackServicesStateAsync(stackStateFile, stackName, servicesState);
        await StackStateStore.SetOperationAsync(
            stackStateFile,
            stackName,
            operationType: operationType,
            operationStatus: operationStatus);
    }

    sealed record SingleProjectControlContext(
        string ProjectDir,
        string StackStateFile,
        string StackName);
}
