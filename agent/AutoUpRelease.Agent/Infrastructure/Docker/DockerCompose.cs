using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    const string DockerCli = "docker";
    const string DockerComposeCli = DockerCli;

    static readonly HashSet<string> NonBlockingServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "migrator"
    };

    public static async Task LoginAsync(string registry, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(registry) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
            return;

        await RunProcessAsync(
            workingDir: Directory.GetCurrentDirectory(),
            fileName: DockerCli,
            args: ["login", registry.Trim(), "-u", username.Trim(), "--password-stdin"],
            stdin: password + Environment.NewLine);
    }

    public static async Task RunAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null,
        IReadOnlyDictionary<string, string>? env = null,
        params string[] args)
    {
        var all = BuildComposeArgs(composeFiles, args);
        await RunProcessAsync(composeDir, DockerComposeCli, all.ToArray(), env: env);
    }

    public static async Task RunAsync(string composeDir, string? stackName, IReadOnlyList<string>? composeFiles = null, params string[] args)
    {
        var all = BuildComposeArgs(composeFiles, args);
        await RunProcessAsync(composeDir, DockerComposeCli, all.ToArray(), env: GetComposeEnv(stackName));
    }

    public static async Task RunPullAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null,
        string? stackName = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var pullArgs = BuildComposeArgs(composeFiles, "pull");
        await RunProcessAsync(composeDir, DockerComposeCli, pullArgs.ToArray(), env: env ?? GetComposeEnv(stackName));
    }

    public static async Task<bool> IsRunningAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null,
        string? stackName = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        try
        {
            var allServicesArgs = BuildComposeArgs(composeFiles, "config", "--services");
            env ??= GetComposeEnv(stackName);
            var (allServicesOut, _, allExit) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, allServicesArgs.ToArray(), env);

            if (allExit != 0)
                return false;

            var allServices = allServicesOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !NonBlockingServices.Contains(s))
                .ToHashSet(StringComparer.Ordinal);

            if (allServices.Count == 0)
                return false;

            var runningArgs = BuildComposeArgs(composeFiles, "ps", "--status", "running", "--services");
            var (runningOut, _, runningExit) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, runningArgs.ToArray(), env);
            var exitedArgs = BuildComposeArgs(composeFiles, "ps", "--status", "exited", "--services");
            var (exitedOut, _, exitedExit) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, exitedArgs.ToArray(), env);

            if (runningExit != 0 || exitedExit != 0)
                return false;

            var runningServices = runningOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(allServices.Contains)
                .ToHashSet(StringComparer.Ordinal);

            var exitedServices = exitedOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(allServices.Contains)
                .ToHashSet(StringComparer.Ordinal);

            return runningServices.Count + exitedServices.Count == allServices.Count;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<Dictionary<string, DockerServiceState>> GetServicesStateAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null,
        string? stackName = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var result = new Dictionary<string, DockerServiceState>(StringComparer.Ordinal);

        try
        {
            var psArgs = BuildComposeArgs(composeFiles, "ps", "--all", "--format", "json");
            var (stdout, _, exit) = await RunProcessCaptureAsync(
                composeDir,
                DockerComposeCli,
                psArgs.ToArray(),
                env ?? GetComposeEnv(stackName));

            if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
                return result;

            var trimmed = stdout.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in doc.RootElement.EnumerateArray())
                        AddServiceState(result, row);
                }
            }
            else
            {
                foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    using var doc = JsonDocument.Parse(line);
                    AddServiceState(result, doc.RootElement);
                }
            }

            return result;
        }
        catch
        {
            return result;
        }
    }

    public static async Task<HashSet<int>> GetPublishedTcpHostPortsAsync()
    {
        try
        {
            var (stdout, _, exit) = await RunProcessCaptureAsync(
                Directory.GetCurrentDirectory(),
                DockerCli,
                ["ps", "--format", "{{.Ports}}"]);

            if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
                return new HashSet<int>();

            var ports = new HashSet<int>();
            var re = new Regex(@"(?<hostPort>\d+)->\d+/tcp", RegexOptions.Compiled);

            foreach (Match m in re.Matches(stdout))
            {
                if (int.TryParse(m.Groups["hostPort"].Value, out var p) && p is >= 1 and <= 65535)
                    ports.Add(p);
            }

            return ports;
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    static void AddServiceState(Dictionary<string, DockerServiceState> result, JsonElement row)
    {
        var service = GetString(row, "Service");
        if (string.IsNullOrWhiteSpace(service))
            return;

        var state = GetString(row, "State") ?? "unknown";
        var health = GetString(row, "Health");
        result[service] = new DockerServiceState(state, string.IsNullOrWhiteSpace(health) ? null : health);
    }

    static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    internal static IReadOnlyDictionary<string, string>? GetComposeEnv(string? stackName)
    {
        if (string.IsNullOrWhiteSpace(stackName))
            return null;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["STACK_NAME"] = stackName.Trim()
        };
    }
}
