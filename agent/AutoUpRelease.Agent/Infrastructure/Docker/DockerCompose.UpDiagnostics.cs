using System.Text;
using System.Text.RegularExpressions;

namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    static readonly Regex ComposeFailedServiceRegex =
        new(@"ERROR:\s+for\s+(?<service>[A-Za-z0-9_.-]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task RunUpDetachedWithDiagnosticsAsync(
        string composeDir,
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyList<string>? composeFiles = null,
        int failedServiceLogsTail = 120,
        Func<Dictionary<string, DockerServiceState>, Task>? onProgress = null,
        TimeSpan? progressPollInterval = null,
        string? stackName = null)
    {
        env ??= GetComposeEnv(stackName);
        var upArgs = BuildComposeArgs(composeFiles, "up", "-d");
        using var progressCts = new CancellationTokenSource();
        Task? progressTask = null;

        if (onProgress is not null)
        {
            progressTask = PollServicesStateUntilCancelledAsync(
                composeDir,
                progressPollInterval ?? TimeSpan.FromSeconds(2),
                onProgress,
                composeFiles,
                progressCts.Token,
                stackName,
                env);
        }

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            (stdout, stderr, exitCode) = await RunProcessCaptureAsync(
                composeDir,
                DockerComposeCli,
                upArgs.ToArray(),
                env,
                timeout: TimeSpan.FromMinutes(5));
        }
        catch (TimeoutException ex)
        {
            if (progressTask is not null)
            {
                progressCts.Cancel();
                await progressTask;
            }

            await LogServicesDiagnosticsAsync(composeDir, env, composeFiles, stackName);
            throw new InvalidOperationException(ex.Message, ex);
        }

        if (progressTask is not null)
        {
            progressCts.Cancel();
            await progressTask;
        }

        await LogPostUpSnapshotAsync(composeDir, env, composeFiles, stackName, stdout, stderr);

        if (exitCode == 0)
            return;

        var composeError = BuildComposeErrorText(stdout, stderr, exitCode);
        var failedService = TryGetFailedServiceName(composeError);
        if (string.IsNullOrWhiteSpace(failedService))
            throw new InvalidOperationException(composeError);

        var serviceLogs = await TryGetServiceLogsAsync(composeDir, failedService, composeFiles, failedServiceLogsTail, stackName);
        if (string.IsNullOrWhiteSpace(serviceLogs))
            throw new InvalidOperationException(composeError);

        throw new InvalidOperationException(
            $"{composeError}{Environment.NewLine}{Environment.NewLine}Последние логи сервиса '{failedService}':{Environment.NewLine}{serviceLogs}");
    }

    public static async Task LogServicesDiagnosticsAsync(
        string composeDir,
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyList<string>? composeFiles = null,
        string? stackName = null,
        int tailLines = 40)
    {
        env ??= GetComposeEnv(stackName);
        var servicesState = await GetServicesStateAsync(composeDir, composeFiles, stackName, env);
        if (servicesState.Count > 0)
        {
            var summary = string.Join(
                ", ",
                servicesState
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}={kv.Value.State}{(string.IsNullOrWhiteSpace(kv.Value.Health) ? string.Empty : $"/{kv.Value.Health}")}"));
            Console.WriteLine($"[docker] diagnostics services: {summary}");
        }

        var psArgs = BuildComposeArgs(composeFiles, "ps", "--all");
        var (psStdout, psStderr, psExitCode) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, psArgs.ToArray(), env);
        Console.WriteLine(
            $"[docker] diagnostics ps exit={psExitCode} output={FormatDiagnosticBlock(psStdout, psStderr)}");

        foreach (var serviceName in servicesState.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var serviceLogs = await TryGetServiceLogsAsync(composeDir, serviceName, composeFiles, tailLines, stackName);
            if (!string.IsNullOrWhiteSpace(serviceLogs))
                Console.WriteLine($"[docker] diagnostics logs service={serviceName}:{Environment.NewLine}{serviceLogs}");
        }
    }

    static string BuildComposeErrorText(string stdout, string stderr, int exitCode)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.AppendLine(stderr.Trim());

        var output = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(output))
            output = $"docker compose завершился с кодом {exitCode}";

        return Truncate(output, 6000);
    }

    static string? TryGetFailedServiceName(string composeErrorText)
    {
        if (string.IsNullOrWhiteSpace(composeErrorText))
            return null;

        var match = ComposeFailedServiceRegex.Match(composeErrorText);
        return match.Success ? match.Groups["service"].Value.Trim() : null;
    }

    static async Task<string?> TryGetServiceLogsAsync(
        string composeDir,
        string serviceName,
        IReadOnlyList<string>? composeFiles,
        int tailLines,
        string? stackName)
    {
        var env = GetComposeEnv(stackName);
        var logsArgs = BuildComposeArgs(
            composeFiles,
            "logs",
            "--no-color",
            "--tail",
            Math.Max(1, tailLines).ToString(),
            serviceName);

        var (stdout, stderr, _) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, logsArgs.ToArray(), env);

        if (!string.IsNullOrWhiteSpace(stdout))
            return Truncate(stdout.Trim(), 6000);

        if (!string.IsNullOrWhiteSpace(stderr))
            return Truncate(stderr.Trim(), 6000);

        return null;
    }

    static async Task LogPostUpSnapshotAsync(
        string composeDir,
        IReadOnlyDictionary<string, string>? env,
        IReadOnlyList<string>? composeFiles,
        string? stackName,
        string stdout,
        string stderr)
    {
        if (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr))
            Console.WriteLine($"[docker] post-up output={FormatDiagnosticBlock(stdout, stderr)}");

        var servicesState = await GetServicesStateAsync(composeDir, composeFiles, stackName, env);
        if (servicesState.Count == 0)
        {
            Console.WriteLine("[docker] post-up services snapshot=<empty>");
            return;
        }

        var summary = string.Join(
            ", ",
            servicesState
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value.State}{(string.IsNullOrWhiteSpace(kv.Value.Health) ? string.Empty : $"/{kv.Value.Health}")}"));
        Console.WriteLine($"[docker] post-up services: {summary}");
    }

    static string FormatDiagnosticBlock(string stdout, string stderr)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.AppendLine(stderr.Trim());

        var combined = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(combined) ? "<empty>" : Truncate(combined, 6000);
    }

    static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            return value;

        return value[..maxChars] + $"{Environment.NewLine}... (output truncated)";
    }
}
