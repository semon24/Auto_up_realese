using System.Collections.Concurrent;
using System.Text.Json;

namespace AutoUpRelease.Agent;

public static class AgentsStateFileBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    static SemaphoreSlim GetFileLock(string filePath) =>
        FileLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new SemaphoreSlim(1, 1));

    public static async Task<int> BuildAggregatedAgentsStateAsync(
        string deployProjectsDir,
        string stateFileName,
        string aggregatedFilePath,
        string? agentHostName = null,
        ServiceLinkEnvKeys? serviceLinkEnvKeys = null)
    {
        var outputLock = GetFileLock(aggregatedFilePath);
        await outputLock.WaitAsync();
        try
        {
            var stacks = new Dictionary<string, AggregatedStackEntry>(StringComparer.Ordinal);
            if (Directory.Exists(deployProjectsDir))
            {
                foreach (var stackDir in Directory.GetDirectories(deployProjectsDir))
                {
                    var stateFilePath = Path.Combine(stackDir, stateFileName);
                    if (!File.Exists(stateFilePath))
                        continue;

                    var stateLock = GetFileLock(stateFilePath);
                    await stateLock.WaitAsync();
                    try
                    {
                        var state = await ReadStateFileAsync(stateFilePath);
                        foreach (var kv in state.Stack)
                        {
                            var aggregatedEntry = ToAggregatedEntry(kv.Value);
                            if (aggregatedEntry.ServiceLinks is null && serviceLinkEnvKeys is not null)
                            {
                                var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, kv.Key);
                                aggregatedEntry.ServiceLinks = DeployEnvLinks.TryRead(stackEnvFile, serviceLinkEnvKeys);
                            }
                            stacks[kv.Key] = aggregatedEntry;
                        }
                    }
                    finally
                    {
                        stateLock.Release();
                    }
                }
            }

            var dir = Path.GetDirectoryName(aggregatedFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var payload = new AggregatedAgentsStateRoot
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                AgentHostName = agentHostName?.Trim() ?? string.Empty,
                Stacks = stacks
            };

            await WriteAggregatedFileAsync(aggregatedFilePath, payload);
            return stacks.Count;
        }
        finally
        {
            outputLock.Release();
        }
    }

    public static async Task<int> BuildSingleProjectAgentsStateAsync(
        string projectDir,
        string stateFileName,
        string aggregatedFilePath,
        string? agentHostName = null)
    {
        var outputLock = GetFileLock(aggregatedFilePath);
        await outputLock.WaitAsync();
        try
        {
            var stacks = new Dictionary<string, AggregatedStackEntry>(StringComparer.Ordinal);
            var stateFilePath = Path.Combine(projectDir, stateFileName);

            if (File.Exists(stateFilePath))
            {
                var stateLock = GetFileLock(stateFilePath);
                await stateLock.WaitAsync();
                try
                {
                    var state = await ReadStateFileAsync(stateFilePath);
                    foreach (var kv in state.Stack)
                        stacks[kv.Key] = ToAggregatedEntry(kv.Value);
                }
                finally
                {
                    stateLock.Release();
                }
            }

            var dir = Path.GetDirectoryName(aggregatedFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var payload = new AggregatedAgentsStateRoot
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                AgentHostName = agentHostName?.Trim() ?? string.Empty,
                Stacks = stacks
            };

            await WriteAggregatedFileAsync(aggregatedFilePath, payload);
            return stacks.Count;
        }
        finally
        {
            outputLock.Release();
        }
    }

    static async Task<StateRoot> ReadStateFileAsync(string stateFilePath)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(stateFilePath);
            var model = JsonSerializer.Deserialize<StateRoot>(raw, JsonOptions);
            return model ?? new StateRoot();
        }
        catch
        {
            return new StateRoot();
        }
    }

    static async Task WriteAggregatedFileAsync(string filePath, AggregatedAgentsStateRoot model)
    {
        var temp = filePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(model, JsonOptions));
        File.Move(temp, filePath, overwrite: true);
    }

    static AggregatedStackEntry ToAggregatedEntry(StackEntry entry)
    {
        var running = entry.Services.Values.Any(s =>
            string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase));

        return new AggregatedStackEntry
        {
            Running = running,
            Version = entry.Version,
            OperationType = entry.Operation?.Type,
            OperationStatus = entry.Operation?.Status,
            OperationError = entry.Operation?.Error,
            ServiceLinks = entry.ServiceLinks,
            ServiceDomains = entry.ServiceDomains?.ToList(),
            Domain = entry.Domain,
            Services = entry.Services.ToDictionary(
                kv => kv.Key,
                kv => new DockerServiceState(kv.Value.State, kv.Value.Health),
                StringComparer.Ordinal),
            Ports = entry.Ports.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        };
    }

    sealed class AggregatedAgentsStateRoot
    {
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public string AgentHostName { get; set; } = string.Empty;
        public Dictionary<string, AggregatedStackEntry> Stacks { get; set; } = new(StringComparer.Ordinal);
    }

    sealed class AggregatedStackEntry
    {
        public bool Running { get; set; }
        public string? Version { get; set; }
        public string? OperationType { get; set; }
        public string? OperationStatus { get; set; }
        public string? OperationError { get; set; }
        public DeployServiceLinks? ServiceLinks { get; set; }
        public Dictionary<string, DockerServiceState> Services { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Ports { get; set; } = new(StringComparer.Ordinal);
        public List<string>? ServiceDomains { get; set; }
        public string? Domain { get; set; }
    }

    sealed class StateRoot
    {
        public Dictionary<string, StackEntry> Stack { get; set; } = new(StringComparer.Ordinal);
    }

    sealed class StackEntry
    {
        public string? Version { get; set; }
        public Dictionary<string, DockerServiceState> Services { get; set; } = new(StringComparer.Ordinal);
        public StackOperation? Operation { get; set; }
        public Dictionary<string, int> Ports { get; set; } = new(StringComparer.Ordinal);
        public DeployServiceLinks? ServiceLinks { get; set; }
        public List<string>? ServiceDomains { get; set; }
        public string? Domain { get; set; }
    }

    sealed class StackOperation
    {
        public string Type { get; set; } = "start";
        public string Status { get; set; } = "in_progress";
        public string? Error { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
