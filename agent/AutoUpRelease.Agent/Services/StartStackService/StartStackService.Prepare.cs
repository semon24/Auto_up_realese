using AutoUpRelease.Agent;
using System.Text.RegularExpressions;

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

    private async Task<string?> AllocatePortsAsync(
        StartStackContext context,
        CancellationToken ct)
    {
        var keys = ResolvePortKeys(context.StackEnvFile);
        if (keys.Count == 0)
            return "В .env не найдено ни одной переменной вида *_PORT или *_PORT_<N>";

        var allocatedPorts = await PortAllocator.AllocateAndWriteEnvAsync(
            context.DeployProjectsDir,
            context.StateFileName,
            context.Tag,
            context.StackEnvFile,
            keys,
            scanMin: 1024,
            scanMax: 65535,
            ct);

        await StackStateStore.SetAllocatedPortsAsync(context.StackStateFile, context.Tag, allocatedPorts);
        return null;
    }

    private static List<string> ResolvePortKeys(string stackEnvFile)
    {
        if (!File.Exists(stackEnvFile))
            return new List<string>();

        var envText = File.ReadAllText(stackEnvFile);
        var matches = Regex.Matches(
            envText,
            @"^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*_PORT(?:_[0-9]+)?)\s*=.*$",
            RegexOptions.Multiline);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(key))
                keys.Add(key);
        }

        return keys.ToList();
    }
}
