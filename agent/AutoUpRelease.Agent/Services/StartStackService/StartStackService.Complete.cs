using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private async Task<StartStackResult> CompleteSuccessAsync(
        StartStackContext context,
        IReadOnlyDictionary<string, DockerServiceState> servicesState)
    {
        var serviceLinks = DeployEnvLinks.TryRead(context.StackEnvFile, _options.ServiceLinkEnvKeys);
        await StackStateStore.SaveStackServicesStateAsync(context.StackStateFile, context.StackName, servicesState);
        await StackStateStore.SetOperationAsync(
            context.StackStateFile,
            context.StackName,
            operationType: "start",
            operationStatus: "success");
        await StackStateStore.SetServiceLinksAsync(context.StackStateFile, context.StackName, serviceLinks);
        await AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            context.DeployProjectsDir,
            context.StateFileName,
            _options.AgentsJsonFilePath,
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);

        return StartStackResult.OkResult(serviceLinks);
    }

    private async Task<StartStackResult> FailAndCleanupAsync(StartStackContext context, string error)
    {
        await StackStateStore.SetOperationAsync(
            context.StackStateFile,
            context.StackName,
            operationType: "start",
            operationStatus: "deleting",
            error: error);
        await AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            context.DeployProjectsDir,
            context.StateFileName,
            _options.AgentsJsonFilePath,
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);

        await StackCleanupService.CleanupStackAsync(context.StackDir, context.StackName);
        await AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            context.DeployProjectsDir,
            context.StateFileName,
            _options.AgentsJsonFilePath,
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);
        return StartStackResult.Fail(error);
    }
}
