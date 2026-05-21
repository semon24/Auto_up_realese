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

            await StackStateStore.SetOperationAsync(
                stackStateFile,
                tag,
                operationType: "stop",
                operationStatus: "deleting");
            await RebuildAgentsStateAsync();

            Console.WriteLine("[stop-stack] этап=cleanup");
            await StackCleanupService.CleanupStackAsync(stackDir);

            Console.WriteLine("[stop-stack] этап=rebuild_state");
            await RebuildAgentsStateAsync();

            Console.WriteLine("[stop-stack] этап=complete_success");
            return StopStackResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[stop-stack] этап=exception message={ex.Message}");
            return StopStackResult.Fail(ex.Message);
        }
    }

    Task RebuildAgentsStateAsync() =>
        AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            _options.ProjectDeploymentPath.Trim(),
            _options.StateProjectFileName.Trim(),
            _options.AgentsJsonFilePath.Trim(),
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);
}
