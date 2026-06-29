using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.SingleProjectStateSyncService;

public sealed class SingleProjectStateSyncService
{
    private readonly AppOptions _options;

    public SingleProjectStateSyncService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsSingleProjectWorkspaceMode)
            return 0;

        if (_options.NeedsNewSingleProjectInitialization)
            return 0;

        var projectDir = _options.ProjectDeploymentPath.Trim();
        var stackStateFile = Path.Combine(projectDir, _options.StateProjectFileName.Trim());
        var stackEnvFile = Path.Combine(projectDir, ".env");
        var hasLocalComposeFile = AppOptions.HasLocalComposeFile(projectDir);
        var servicesStateByStackName = new Dictionary<string, Dictionary<string, DockerServiceState>>(StringComparer.Ordinal);


        var stackNames = await GetOrCreateStackNamesAsync(stackStateFile);
        var links = DeployEnvLinks.TryRead(stackEnvFile, _options.ServiceLinkEnvKeys);
        var version = EnvFile.ReadTag(stackEnvFile, _options.ImageEnvKey);
        var domain = EnvFile.ReadTag(stackEnvFile, "DOMAIN");
        var serviceDomains = BuildServiceDomains(stackEnvFile, domain);

        var updated = 0;
        foreach (var stackName in stackNames)
        {
            if (!servicesStateByStackName.TryGetValue(stackName, out var servicesState))
            {
                servicesState = hasLocalComposeFile
                    ? await DockerCompose.GetServicesStateAsync(
                        projectDir,
                        stackName: stackName)
                    : new Dictionary<string, DockerServiceState>(StringComparer.Ordinal);

                servicesStateByStackName[stackName] = servicesState;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await StackStateStore.SetServiceLinksAsync(stackStateFile, stackName, links);
            await StackStateStore.SetVersionAsync(stackStateFile, stackName, version);
            await StackStateStore.SetDomainAsync(stackStateFile, stackName, domain);
            if (serviceDomains.Count > 0)
            {
                await StackStateStore.SetServiceDomainsAsync(
                    stackStateFile,
                    stackName,
                    serviceDomains);
            }
            await StackStateStore.SaveStackServicesStateAsync(stackStateFile, stackName, servicesState);
            if (_options.IsSingleProjectMode &&
                HasAllBlockingServicesRunning(servicesState) &&
                await CanMarkStartSuccessAsync(stackStateFile, stackName))
            {
                await StackStateStore.SetOperationAsync(
                    stackStateFile,
                    stackName,
                    operationType: "start",
                    operationStatus: "success");
            }
            updated++;
        }

        return updated;
    }

    async Task<List<string>> GetOrCreateStackNamesAsync(string stackStateFile)
    {
        if (!File.Exists(stackStateFile))
            return new List<string> { CreateInitialSingleProjectStackName() };

        var stackNames = await StackStateStore.GetStackNamesAsync(stackStateFile);
        if (stackNames.Count > 0)
            return stackNames;

        return new List<string> { CreateInitialSingleProjectStackName() };
    }

    string CreateInitialSingleProjectStackName()
    {
        var configured = _options.StackSingleName?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return $"{ProjectLayoutResolver.GetSingleProjectName(_options)}{Random.Shared.Next(100000, 1000000)}";
    }

    static List<string> BuildServiceDomains(string stackEnvFile, string? rootDomain)
    {
        var normalizedRootDomain = rootDomain?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedRootDomain))
            return new List<string>();

        if (System.Net.IPAddress.TryParse(normalizedRootDomain, out _))
            return new List<string>();

        var serviceNames = new[]
        {
            EnvFile.ReadTag(stackEnvFile, "ADMIN_SERVICE"),
            EnvFile.ReadTag(stackEnvFile, "SERVER_SERVICE"),
            EnvFile.ReadTag(stackEnvFile, "PORTAL_SERVICE"),
            EnvFile.ReadTag(stackEnvFile, "CALL_SERVICE")
        };

        return serviceNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToLowerInvariant())
            .Select(x => $"{x}.{normalizedRootDomain}")
            .Distinct()
            .ToList();
    }

    static bool HasAllBlockingServicesRunning(IReadOnlyDictionary<string, DockerServiceState> services)
    {
        var blockingServices = services
            .Where(kv => !string.Equals(kv.Key, "migrator", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return blockingServices.Count > 0 &&
            blockingServices.All(kv => string.Equals(kv.Value.State, "running", StringComparison.OrdinalIgnoreCase));
    }

    static async Task<bool> CanMarkStartSuccessAsync(string stackStateFile, string stackName)
    {
        var status = await StackStateStore.GetOperationStatusAsync(stackStateFile, stackName);
        return string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase);
    }
}
