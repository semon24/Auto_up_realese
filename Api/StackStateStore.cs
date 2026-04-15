using System.Text.Json;
using System.Collections.Concurrent;

namespace AutoUpRelease.Api;

public static class StackStateStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    static SemaphoreSlim GetFileLock(string stateFilePath) =>
        FileLocks.GetOrAdd(Path.GetFullPath(stateFilePath), _ => new SemaphoreSlim(1, 1));

    public static async Task SaveStackServicesStateAsync(string stateFilePath, string stackName, IReadOnlyDictionary<string, DockerServiceState> services)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var model = await ReadModelAsync(stateFilePath);
            model.Stack.TryGetValue(stackName, out var existingEntry);
            model.Stack[stackName] = new StackEntry
            {
                Services = services.ToDictionary(
                    kv => kv.Key,
                    kv => new DockerServiceState(kv.Value.State, kv.Value.Health),
                    StringComparer.Ordinal),
                Operation = existingEntry?.Operation,
                Ports = existingEntry?.Ports ?? new Dictionary<string, int>(StringComparer.Ordinal)
            };

            await WriteModelAsync(stateFilePath, model);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task SetOperationAsync(
        string stateFilePath,
        string stackName,
        string operationType,
        string operationStatus,
        string? error = null)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var model = await ReadModelAsync(stateFilePath);
            if (!model.Stack.TryGetValue(stackName, out var entry))
                entry = new StackEntry();

            entry.Operation = new StackOperation
            {
                Type = operationType,
                Status = operationStatus,
                Error = error,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            model.Stack[stackName] = entry;
            await WriteModelAsync(stateFilePath, model);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task SetAllocatedPortsAsync(
        string stateFilePath,
        string stackName,
        IReadOnlyDictionary<string, int> ports)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var model = await ReadModelAsync(stateFilePath);
            if (!model.Stack.TryGetValue(stackName, out var entry))
                entry = new StackEntry();

            entry.Ports = ports.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            model.Stack[stackName] = entry;
            await WriteModelAsync(stateFilePath, model);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<Dictionary<string, int>> GetAllocatedPortsAsync(string stateFilePath, string stackName)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var model = await ReadModelAsync(stateFilePath);
            if (!model.Stack.TryGetValue(stackName, out var entry) || entry.Ports.Count == 0)
                return new Dictionary<string, int>(StringComparer.Ordinal);

            return entry.Ports.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<bool> HasAnyRunningServicesAsync(string stateFilePath)
    {
        var model = await ReadModelAsync(stateFilePath);
        foreach (var stack in model.Stack.Values)
        {
            foreach (var service in stack.Services.Values)
            {
                if (string.Equals(service.State, "running", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public static async Task<bool> IsDeletingAsync(string stateFilePath, string stackName)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var model = await ReadModelAsync(stateFilePath);
            if (!model.Stack.TryGetValue(stackName, out var entry))
                return false;

            return string.Equals(entry.Operation?.Status, "deleting", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<string?> GetOperationStatusAsync(string stateFilePath, string stackName)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var model = await ReadModelAsync(stateFilePath);
            if (!model.Stack.TryGetValue(stackName, out var entry))
                return null;

            return entry.Operation?.Status;
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<(bool Running, string? OperationType, string? OperationStatus, string? OperationError)> GetStackRuntimeInfoAsync(
        string stateFilePath,
        string stackName)
    {
        var fileLock = GetFileLock(stateFilePath);
        await fileLock.WaitAsync();
        try
        {
            var model = await ReadModelAsync(stateFilePath);
            if (!model.Stack.TryGetValue(stackName, out var entry))
                return (false, null, null, null);

            var running = entry.Services.Values.Any(s => string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase));
            return (running, entry.Operation?.Type, entry.Operation?.Status, entry.Operation?.Error);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<List<string>> GetStackNamesAsync(string stateFilePath)
    {
        var model = await ReadModelAsync(stateFilePath);
        return model.Stack.Keys.ToList();
    }

    public static async Task<string?> GetAnyRunningStackNameAsync(string stateFilePath)
    {
        var model = await ReadModelAsync(stateFilePath);
        foreach (var entry in model.Stack)
        {
            if (entry.Value.Services.Values.Any(s => string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase)))
                return entry.Key;
        }

        return null;
    }

    static async Task<StateRoot> ReadModelAsync(string stateFilePath)
    {
        if (!File.Exists(stateFilePath))
            return new StateRoot();

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

    static async Task WriteModelAsync(string stateFilePath, StateRoot model)
    {
        var temp = stateFilePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(model, JsonOptions));
        File.Move(temp, stateFilePath, overwrite: true);
    }

    sealed class StateRoot
    {
        public Dictionary<string, StackEntry> Stack { get; set; } = new(StringComparer.Ordinal);
    }

    sealed class StackEntry
    {
        public Dictionary<string, DockerServiceState> Services { get; set; } = new(StringComparer.Ordinal);
        public StackOperation? Operation { get; set; }
        public Dictionary<string, int> Ports { get; set; } = new(StringComparer.Ordinal);
    }

    sealed class StackOperation
    {
        public string Type { get; set; } = "start";
        public string Status { get; set; } = "in_progress";
        public string? Error { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
