using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using AutoUpRelease.Agent.Commands;
using AutoUpRelease.Agent.Services.DeleteStackService;
using AutoUpRelease.Agent.Services.RestartStackService;
using AutoUpRelease.Agent.Services.SingleProjectControlService;
using AutoUpRelease.Agent.Services.SingleProjectStateSyncService;
using AutoUpRelease.Agent.Services.StartStackService;
using AutoUpRelease.Agent.Services.StaleStartRecoveryService;
using AutoUpRelease.Agent.Services.StopStackService;
using AutoUpRelease.Agent.Services.UpdateVersionService;


namespace AutoUpRelease.Agent;

public sealed class AgentWorker : BackgroundService
{
    static readonly TimeSpan AggregationInterval = TimeSpan.FromSeconds(10);
    private readonly AppOptions _appOptions;
    private readonly StartStackService _startStackService;
    private readonly DeleteStackService _deleteStackService;
    private readonly RestartStackService _restartStackService;
    private readonly StopStackService _stopStackService;
    private readonly UpdateVersionService _updateVersionService;
    private readonly StaleStartRecoveryService _staleStartRecoveryService;
    private readonly SingleProjectControlService _singleProjectControlService;
    private readonly SingleProjectStateSyncService _singleProjectStateSyncService;

    public AgentWorker(
        IOptions<AppOptions> appOptions,
        StartStackService startStackService,
        DeleteStackService deleteStackService,
        RestartStackService restartStackService,
        StopStackService stopStackService,
        UpdateVersionService updateVersionService,
        SingleProjectControlService singleProjectControlService,
        StaleStartRecoveryService staleStartRecoveryService,
        SingleProjectStateSyncService singleProjectStateSyncService)
    {
        _appOptions = appOptions.Value;
        _startStackService = startStackService;
        _deleteStackService = deleteStackService;
        _restartStackService = restartStackService;
        _stopStackService = stopStackService;
        _updateVersionService = updateVersionService;
        _singleProjectControlService = singleProjectControlService;
        _staleStartRecoveryService = staleStartRecoveryService;
        _singleProjectStateSyncService = singleProjectStateSyncService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = _appOptions.AgentHostName;
        try
        {
            await RunSignalRSessionAsync(hostName, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] ошибка: {ex.Message}");
        }

        Console.WriteLine("[agent] выход");
    }

    async Task RunSignalRSessionAsync(string hostName, CancellationToken stoppingToken)
    {
        var agentMode = _appOptions.IsSingleProjectMode
            ? "single-project"
            : _appOptions.IsNewSingleProjectMode
                ? "new-solo-project"
                : _appOptions.Mode;
        var connection = AgentSignalRConnection.Create(hostName, _appOptions.Type, agentMode);
        var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var aggregationCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? aggregationTask = null;

        connection.Reconnecting += ex =>
        {
            var reason = ex?.Message ?? "без ошибки";
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR reconnecting: {reason}");
            return Task.CompletedTask;
        };

        connection.Reconnected += newConnectionId =>
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR reconnected, connectionId={newConnectionId ?? "<null>"}");
            return Task.CompletedTask;
        };

        connection.Closed += ex =>
        {
            if (ex is null)
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR соединение закрыто");
            else
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR соединение закрыто с ошибкой: {ex.Message}");
            disconnectedTcs.TrySetResult();
            return Task.CompletedTask;
        };

        AgentSignalRPasswordMessages.Register(connection, _appOptions);
        DockerComposeUpSignalRMessages.Register(connection, _startStackService, _singleProjectControlService, new OptionsWrapper<AppOptions>(_appOptions));
        DockerComposeDownSignalRMessages.Register(connection, _deleteStackService);
        DockerComposeRestartSignalRMessages.Register(connection, _restartStackService, _singleProjectControlService, new OptionsWrapper<AppOptions>(_appOptions));
        DockerComposeStopSignalRMessages.Register(connection, _stopStackService, _singleProjectControlService, new OptionsWrapper<AppOptions>(_appOptions));
        UpdateVersionServiceSignalRMessages.Register(connection, _updateVersionService);
        UpdateHarborTagsSignalRMessages.Register(connection, _appOptions);
        try
        {
            await connection.StartAsync(stoppingToken);
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR подключено: {hostName}");
            aggregationTask = RunAggregatedAgentsStateLoopAsync(connection, aggregationCts.Token);
            await disconnectedTcs.Task.WaitAsync(stoppingToken);
        }
        finally
        {
            aggregationCts.Cancel();
            if (aggregationTask is not null)
            {
                try
                {
                    await aggregationTask;
                }
                catch (OperationCanceledException) when (aggregationCts.IsCancellationRequested)
                {
                }
            }

            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
            }
        }
    }

    async Task RunAggregatedAgentsStateLoopAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] запуск цикла агрегации state-файла: интервал={AggregationInterval.TotalSeconds:0}с, output={_appOptions.AgentsJsonFilePath.Trim()}");
        await BuildAggregatedAgentsStateSafeAsync(connection, cancellationToken);
        using var timer = new PeriodicTimer(AggregationInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await BuildAggregatedAgentsStateSafeAsync(connection, cancellationToken);
    }

    async Task BuildAggregatedAgentsStateSafeAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await _staleStartRecoveryService.RecoverAsync(cancellationToken);
            var projectStatesUpdated = await RefreshProjectStatesAsync(cancellationToken);

            var stacksCount = 0;
            if (_appOptions.IsSingleProjectWorkspaceMode)
            {
                stacksCount = await AgentsStateFileBuilder.BuildSingleProjectAgentsStateAsync(
                    _appOptions.ProjectDeploymentPath.Trim(),
                    _appOptions.StateProjectFileName.Trim(),
                    _appOptions.AgentsJsonFilePath.Trim(),
                    _appOptions.AgentHostName);
            }
            else
            {
                stacksCount = await AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
                    _appOptions.ProjectDeploymentPath.Trim(),
                    _appOptions.StateProjectFileName.Trim(),
                    _appOptions.AgentsJsonFilePath.Trim(),
                    _appOptions.AgentHostName,
                    _appOptions.ServiceLinkEnvKeys);
            }
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] агрегированный state-файл обновлен: stacks={stacksCount}, projectStatesUpdated={projectStatesUpdated}, output={_appOptions.AgentsJsonFilePath.Trim()}");

            try
            {
                await AgentServicesSnapshotSignalRMessages.SendFromFileAsync(
                    connection,
                    _appOptions.AgentsJsonFilePath.Trim(),
                    cancellationToken);
                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] snapshot отправлен на сервер: stacks={stacksCount}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] не удалось отправить snapshot на сервер: {ex.Message}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] не удалось собрать агрегированный state-файл: {ex.Message}");
        }
    }

    async Task<int> RefreshProjectStatesAsync(CancellationToken cancellationToken)
    {
        var deployDir = _appOptions.ProjectDeploymentPath.Trim();
        var stateFileName = _appOptions.StateProjectFileName.Trim();

        if (_appOptions.IsSingleProjectWorkspaceMode)
            return await _singleProjectStateSyncService.SyncAsync(cancellationToken);

        if (!Directory.Exists(deployDir))
            return 0;

        var updated = 0;
        foreach (var stackDir in Directory.GetDirectories(deployDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!File.Exists(stackStateFile))
                continue;

            var stackNames = await StackStateStore.GetStackNamesAsync(stackStateFile);
            if (stackNames.Count == 0)
                stackNames.Add(Path.GetFileName(stackDir));
            foreach (var stackName in stackNames)
            {
                var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployDir, stackName);
                var links = DeployEnvLinks.TryRead(stackEnvFile, _appOptions.ServiceLinkEnvKeys);
                await StackStateStore.SetServiceLinksAsync(stackStateFile, stackName, links);
                updated++;
            }
        }

        return updated;
    }
}
