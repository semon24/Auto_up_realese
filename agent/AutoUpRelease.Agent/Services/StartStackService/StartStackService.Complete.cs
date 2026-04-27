using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private async Task<StartStackResult> CompleteSuccessAsync(
        StartStackContext context,
        IReadOnlyDictionary<string, DockerServiceState> servicesState)
    {
        await StackStateStore.SaveStackServicesStateAsync(context.StackStateFile, context.Tag, servicesState);
        await StackStateStore.SetOperationAsync(
            context.StackStateFile,
            context.Tag,
            operationType: "start",
            operationStatus: "success");

        var serviceLinks = DeployEnvLinks.TryRead(context.StackEnvFile, _options.ServiceLinkEnvKeys);
        return StartStackResult.OkResult(serviceLinks);
    }

    private async Task<StartStackResult> FailAndCleanupAsync(StartStackContext context, string error)
    {
        await StackStateStore.SetOperationAsync(
            context.StackStateFile,
            context.Tag,
            operationType: "start",
            operationStatus: "deleting",
            error: error);

        await DockerComposeFullCleanup.CleanupStackAsync(context.StackDir);
        return StartStackResult.Fail(error);
    }
}
