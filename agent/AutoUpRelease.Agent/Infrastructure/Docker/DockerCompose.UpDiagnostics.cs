using System.Text;
using System.Text.RegularExpressions;

namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    static readonly Regex ComposeFailedServiceRegex =
        new(@"ERROR:\s+for\s+(?<service>[A-Za-z0-9_.-]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task RunUpDetachedWithDiagnosticsAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null,
        int failedServiceLogsTail = 120,
        Func<Dictionary<string, DockerServiceState>, Task>? onProgress = null,
        TimeSpan? progressPollInterval = null)
    {
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
                progressCts.Token);
        }

        var (stdout, stderr, exitCode) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, upArgs.ToArray());

        if (progressTask is not null)
        {
            progressCts.Cancel();
            await progressTask;
        }

        if (exitCode == 0)
            return;

        var composeError = BuildComposeErrorText(stdout, stderr, exitCode);
        var failedService = TryGetFailedServiceName(composeError);
        if (string.IsNullOrWhiteSpace(failedService))
            throw new InvalidOperationException(composeError);

        var serviceLogs = await TryGetServiceLogsAsync(composeDir, failedService, composeFiles, failedServiceLogsTail);
        if (string.IsNullOrWhiteSpace(serviceLogs))
            throw new InvalidOperationException(composeError);

        throw new InvalidOperationException(
            $"{composeError}{Environment.NewLine}{Environment.NewLine}Последние логи сервиса '{failedService}':{Environment.NewLine}{serviceLogs}");
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
        int tailLines)
    {
        var logsArgs = BuildComposeArgs(
            composeFiles,
            "logs",
            "--no-color",
            "--tail",
            Math.Max(1, tailLines).ToString(),
            serviceName);

        var (stdout, stderr, _) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, logsArgs.ToArray());

        if (!string.IsNullOrWhiteSpace(stdout))
            return Truncate(stdout.Trim(), 6000);

        if (!string.IsNullOrWhiteSpace(stderr))
            return Truncate(stderr.Trim(), 6000);

        return null;
    }

    static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            return value;

        return value[..maxChars] + $"{Environment.NewLine}... (output truncated)";
    }
}
