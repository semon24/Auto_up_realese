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
            context.Tag,
            operationType: "start",
            operationStatus: "in_progress");

        await DockerCompose.PrepareStackResources(
            context.Tag,
            context.StackDir,
            context.StackEnvFile,
            _options.ImageEnvKey,
            _options.DatabasePasswordEnvKeys);
    }

    private async Task<string?> AllocatePortsIfNeededAsync(
        StartStackContext context,
        bool allocatePorts,
        CancellationToken ct)
    {
        if (!allocatePorts)
            return null;

        var keys = (_options.PortAllocation?.Keys ?? new List<string>())
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToList();

        if (keys.Count == 0)
            return "allocatePorts включен, но PortAllocation.Keys пуст";

        var scanMin = _options.PortAllocation?.ScanMin ?? 1;
        var scanMax = _options.PortAllocation?.ScanMax ?? 65535;

        var allocatedPorts = await PortAllocator.AllocateAndWriteEnvAsync(
            context.DeployProjectsDir,
            context.StateFileName,
            context.Tag,
            context.StackEnvFile,
            keys,
            scanMin,
            scanMax,
            ct);

        await StackStateStore.SetAllocatedPortsAsync(context.StackStateFile, context.Tag, allocatedPorts);
        return null;
    }
}
