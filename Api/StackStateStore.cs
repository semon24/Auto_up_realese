using System.Text.Json;

namespace AutoUpRelease.Api;

public static class StackStateStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task WriteAsync(string stateFilePath, string stackName, IReadOnlyDictionary<string, DockerServiceState> services)
    {
        var dir = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var model = await ReadModelAsync(stateFilePath);
        model.Stack[stackName] = new StackEntry
        {
            Services = services.ToDictionary(
                kv => kv.Key,
                kv => new DockerServiceState(kv.Value.State, kv.Value.Health),
                StringComparer.Ordinal)
        };

        await WriteModelAsync(stateFilePath, model);
    }

    public static async Task DeleteStackAsync(string stateFilePath, string stackName)
    {
        var model = await ReadModelAsync(stateFilePath);
        model.Stack.Remove(stackName);
        await WriteModelAsync(stateFilePath, model);
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
    }
}
