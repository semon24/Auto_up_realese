using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.DeleteStackService;

public sealed class DeleteStackService
{
    readonly AppOptions _options;

    public DeleteStackService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task<DeleteStackResult> ExecuteAsync(
        string? rawTag,
        CancellationToken ct = default)
    {
        var tag = rawTag?.Trim();
        if (string.IsNullOrEmpty(tag))
            return DeleteStackResult.Fail("Нужен tag");

        var deployProjectsDir = _options.ProjectDeploymentPath.Trim();
        var stateFileName = _options.StateProjectFileName.Trim();
        var stackDir = StackWorkspaceManager.GetStackDir(deployProjectsDir, tag);
        var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

        if (!Directory.Exists(stackDir))
            return DeleteStackResult.Fail($"Стек с тегом '{tag}' не найден");

        try
        {
            Console.WriteLine($"[delete-stack] этап=begin tag={tag} dir={stackDir}");

            await StackStateStore.SetOperationAsync(
                stackStateFile,
                tag,
                operationType: "delete",
                operationStatus: "deleting");
            await RebuildAgentsStateAsync();

            Console.WriteLine("[delete-stack] этап=cleanup");
            await StackCleanupService.CleanupStackAsync(stackDir, tag);

            Console.WriteLine("[delete-stack] этап=rebuild_state");
            await RebuildAgentsStateAsync();

            Console.WriteLine("[delete-stack] этап=complete_success");
            return DeleteStackResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[delete-stack] этап=exception message={ex.Message}");
            return DeleteStackResult.Fail(ex.Message);
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
