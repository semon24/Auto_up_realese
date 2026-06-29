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

        var rootDomain = EnvFile.ReadTag(context.StackEnvFile, "DOMAIN")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(rootDomain) && !System.Net.IPAddress.TryParse(rootDomain, out _))
        {
            var serviceNames = new[]
            {
                EnvFile.ReadTag(context.StackEnvFile, "ADMIN_SERVICE"),
                EnvFile.ReadTag(context.StackEnvFile, "SERVER_SERVICE"),
                EnvFile.ReadTag(context.StackEnvFile, "PORTAL_SERVICE"),
                EnvFile.ReadTag(context.StackEnvFile, "CALL_SERVICE")
            };

            var serviceDomains = serviceNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim().ToLowerInvariant())
                .Select(x => $"{x}.{rootDomain}")
                .Distinct()
                .ToList();

            if (serviceDomains.Count > 0)
            {
                await StackStateStore.SetServiceDomainsAsync(
                    context.StackStateFile,
                    context.StackName,
                    serviceDomains);
            }
        }

        await BuildAgentsStateAsync(context);

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
        await BuildAgentsStateAsync(context);

        if (!context.IsSingleProjectWorkspace)
        {
            await StackCleanupService.CleanupStackAsync(
                context.StackDir,
                context.StackName,
                context.StackStateFile);
            await BuildAgentsStateAsync(context);
        }
        return StartStackResult.Fail(error);
    }

    async Task BuildAgentsStateAsync(StartStackContext context)
    {
        if (context.IsSingleProjectWorkspace)
        {
            await AgentsStateFileBuilder.BuildSingleProjectAgentsStateAsync(
                context.DeployProjectsDir,
                context.StateFileName,
                _options.AgentsJsonFilePath,
                _options.AgentHostName);
            return;
        }

        await AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
            context.DeployProjectsDir,
            context.StateFileName,
            _options.AgentsJsonFilePath,
            _options.AgentHostName,
            _options.ServiceLinkEnvKeys);
    }
}
