using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private async Task PrepareStackAsync(StartStackContext context, CancellationToken ct)
    {
        await DockerCompose.LoginAsync(_options.RegistryUrl, _options.RegistryUser, _options.RegistryPassword);
        StackWorkspaceManager.EnsureStackWorkspace(context.FolderForCopyDir, context.StackDir);

        if (!File.Exists(context.StackStateFile))
            await File.WriteAllTextAsync(context.StackStateFile, "{}", ct);

        await StackStateStore.SetOperationAsync(
            context.StackStateFile,
            context.StackName,
            operationType: "start",
            operationStatus: "in_progress");
        await StackStateStore.SetVersionAsync(
            context.StackStateFile,
            context.StackName,
            context.Version);
        await StackStateStore.SetDomainAsync(
            context.StackStateFile,
            context.StackName,
            context.Domain
        );
        await DockerCompose.PrepareStackResources(
            context.StackName,
            context.Version,
            context.StackDir,
            context.StackEnvFile,
            _options.ImageEnvKey,
            _options.DatabasePasswordEnvKeys);
    }

    private async Task<string?> AllocatePortsAsync(
        StartStackContext context,
        CancellationToken ct)
    {
        var keys = ResolvePortKeys(_options.PortAllocation);
        if (keys.Count == 0)
            return "Не найдено ни одного ключа для аллокации портов: настрой PortAllocation:Keys";
        
        if (ShouldUseDefaultPorts(context.Domain))
        {
            var defaultPorts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["RABBITMQ_PORT_1"] = 15672,
                ["RABBITMQ_PORT_2"] = 5672,
                ["POSTGRES_PORT"] = 5632,
                ["MASTER_POSTGRES_PORT"] = 5532,
                ["FILE_STORAGE_PORT"] = 52959,
                ["SPEECH_PORT"] = 52303,
                ["SERVER_PORT"] = 53800,
                ["ADMIN_PORT"] = 65401,
                ["CALL_PORT"] = 53620,
                ["PORTAL_PORT"] = 53801,
                ["TELEMETRY_COLLECTOR_PORT_1"] = 4317,
                ["TELEMETRY_COLLECTOR_PORT_2"] = 4318,
            };

            await StackStateStore.SetAllocatedPortsAsync(
                context.StackStateFile,
                context.StackName,
                defaultPorts);
        }
        else
        {
            var allocatedPorts = await PortAllocator.AllocateAsync(
                context.DeployProjectsDir,
                context.StateFileName,
                context.StackName,
                keys,
                scanMin: _options.PortAllocation.ScanMin,
                scanMax: _options.PortAllocation.ScanMax,
                ct);

            await StackStateStore.SetAllocatedPortsAsync(context.StackStateFile, context.StackName, allocatedPorts);
        }

        return null;
    }

    private static List<string> ResolvePortKeys(PortAllocationOptions? options)
    {
        var keys = options?.Keys?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList()
            ?? new List<string>();
        if (keys.Count > 0)
            return keys;

        return (options?.KeysCsv ?? string.Empty)
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
