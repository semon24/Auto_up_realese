using System.Collections.Concurrent;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents;

public sealed class AgentServicesSnapshotStore
{
    readonly ConcurrentDictionary<string, HostSnapshot> _snapshotsByHost = new(StringComparer.Ordinal);

    public int Upsert(string hostName, JsonElement payload)
    {
        var normalizedHostName = hostName.Trim();
        if (normalizedHostName.Length == 0)
            return 0;

        var stacks = ParseStacks(payload);
        _snapshotsByHost[normalizedHostName] = new HostSnapshot
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Stacks = stacks
        };
        return stacks.Count;
    }

    public void RemoveHost(string hostName)
    {
        var normalizedHostName = hostName.Trim();
        if (normalizedHostName.Length == 0)
            return;
        _snapshotsByHost.TryRemove(normalizedHostName, out _);
    }

    public Dictionary<string, StackSnapshot> GetFreshStacks(TimeSpan maxAge)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, StackSnapshot>(StringComparer.Ordinal);
        foreach (var hostEntry in _snapshotsByHost)
        {
            var hostSnapshot = hostEntry.Value;
            if (now - hostSnapshot.UpdatedAtUtc > maxAge)
                continue;

            foreach (var stackEntry in hostSnapshot.Stacks)
                result[stackEntry.Key] = stackEntry.Value;
        }

        return result;
    }

    public Dictionary<string, Dictionary<string, StackSnapshot>> GetFreshStacksByHost(TimeSpan maxAge)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, Dictionary<string, StackSnapshot>>(StringComparer.Ordinal);
        foreach (var hostEntry in _snapshotsByHost)
        {
            if (now - hostEntry.Value.UpdatedAtUtc > maxAge)
                continue;

            result[hostEntry.Key] = new Dictionary<string, StackSnapshot>(
                hostEntry.Value.Stacks,
                StringComparer.Ordinal);
        }

        return result;
    }

    static Dictionary<string, StackSnapshot> ParseStacks(JsonElement payload)
    {
        var result = new Dictionary<string, StackSnapshot>(StringComparer.Ordinal);
        if (payload.ValueKind != JsonValueKind.Object)
            return result;
        if (!TryGetObjectProperty(payload, "stacks", out var stacksElement) &&
            !TryGetObjectProperty(payload, "stack", out stacksElement))
            return result;

        foreach (var stackProp in stacksElement.EnumerateObject())
        {
            var tag = stackProp.Name.Trim();
            if (tag.Length == 0 || stackProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            var stack = stackProp.Value;
            var services = ParseServices(stack);
            var running = GetBoolean(stack, "running");
            var version = GetString(stack, "version", "Version");
            var operationType = GetString(stack, "operationType");
            var operationStatus = GetString(stack, "operationStatus");
            var operationError = GetString(stack, "operationError");
            var serviceLinks = ParseServiceLinks(stack);
            var serviceDomains = ParseStringArray(stack, "serviceDomains");


            result[tag] = new StackSnapshot
            {
                Running = running || services.Values.Any(s => string.Equals(s.State, "running", StringComparison.OrdinalIgnoreCase)),
                Version = version,
                OperationType = operationType,
                OperationStatus = operationStatus,
                OperationError = operationError,
                ServiceLinks = serviceLinks,
                ServiceDomains = serviceDomains,
                Services = services
            };
        }

        return result;
    }

    static Dictionary<string, string>? ParseServiceLinks(JsonElement stackElement)
    {
        if (!TryGetObjectProperty(stackElement, "serviceLinks", out var linksElement))
            return null;

        var result = ParseLinksObject(linksElement);
        return result.Count == 0 ? null : result;
    }

    static Dictionary<string, DockerServiceState> ParseServices(JsonElement stackElement)
    {
        var result = new Dictionary<string, DockerServiceState>(StringComparer.Ordinal);
        if (!TryGetObjectProperty(stackElement, "services", out var servicesElement))
            return result;

        foreach (var serviceProp in servicesElement.EnumerateObject())
        {
            var serviceName = serviceProp.Name.Trim();
            if (serviceName.Length == 0 || serviceProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            var state = GetString(serviceProp.Value, "state", "State") ?? "unknown";
            var health = GetString(serviceProp.Value, "health", "Health");
            result[serviceName] = new DockerServiceState(state, string.IsNullOrWhiteSpace(health) ? null : health);
        }

        return result;
    }

    static List<string>? ParseStringArray(JsonElement stackElement, string propertyName)
    {
        if (!TryGetAnyProperty(stackElement, out var arrayElement, propertyName))
            return null;

        if (arrayElement.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var value = item.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            result.Add(value);
        }

        return result.Count == 0 ? null : result;
    }

    static bool GetBoolean(JsonElement element, string propertyName)
    {
        if (!TryGetAnyProperty(element, out var p, propertyName))
            return false;
        return p.ValueKind == JsonValueKind.True || (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var parsed) && parsed);
    }

    static string? GetString(JsonElement element, params string[] propertyNames)
    {
        if (!TryGetAnyProperty(element, out var p, propertyNames) || p.ValueKind != JsonValueKind.String)
            return null;
        return p.GetString();
    }

    static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (TryGetAnyProperty(element, out value, propertyName) && value.ValueKind == JsonValueKind.Object)
            return true;
        value = default;
        return false;
    }

    static bool TryGetAnyProperty(JsonElement element, out JsonElement value, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in element.EnumerateObject())
        {
            foreach (var name in propertyNames)
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    static Dictionary<string, string> ParseLinksObject(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                continue;

            var value = prop.Value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            result[prop.Name] = value;
        }

        return result;
    }

    sealed class HostSnapshot
    {
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public Dictionary<string, StackSnapshot> Stacks { get; init; } = new(StringComparer.Ordinal);
    }
}

public sealed class StackSnapshot
{
    public bool Running { get; init; }
    public string? Version { get; init; }
    public string? OperationType { get; init; }
    public string? OperationStatus { get; init; }
    public string? OperationError { get; init; }
    public Dictionary<string, string>? ServiceLinks { get; init; }
    public Dictionary<string, DockerServiceState> Services { get; init; } = new(StringComparer.Ordinal);
    public List<string>? ServiceDomains { get; init; }
    public IReadOnlyList<SslCertificateInfo>? Certificates { get; init; }
}
