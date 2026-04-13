using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AutoUpRelease.Api;

public static class DockerCompose
{
    const string DockerCli = "docker";
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

    public static async Task RunAsync(string composeDir, params string[] args)
    {
        var all = new List<string> { "compose" };
        all.AddRange(args);
        await RunProcessAsync(composeDir, DockerCli, all.ToArray());
    }

    public static async Task<bool> IsRunningAsync(string composeDir)
    {
        try
        {
            var (allServicesOut, _, allExit) = await RunProcessCaptureAsync(
                composeDir,
                DockerCli,
                "compose", "config", "--services");

            if (allExit != 0)
                return false;

            var allServices = allServicesOut
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !NonBlockingServices.Contains(s))
                .ToHashSet(StringComparer.Ordinal);

            if (allServices.Count == 0)
                return false;

            var (runningOut, _, runningExit) = await RunProcessCaptureAsync(
                composeDir,
                DockerCli,
                "compose", "ps", "--status", "running", "--services");

            if (runningExit != 0)
                return false;

            var runningServices = runningOut
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);

            return allServices.All(runningServices.Contains);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<HashSet<int>> GetPublishedTcpHostPortsAsync()
    {
        try
        {
            var (stdout, _, exit) = await RunProcessCaptureAsync(
                Directory.GetCurrentDirectory(),
                DockerCli,
                "ps", "--format", "{{.Ports}}");

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

    static async Task RunProcessAsync(string workingDir, string fileName, string[] args, string? stdin = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p == null)
            throw new InvalidOperationException($"Не удалось запустить {fileName}");

        if (stdin != null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }

        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
        {
            var msg = string.IsNullOrWhiteSpace(err)
                ? $"{fileName} завершился с кодом {p.ExitCode}"
                : err.Trim();
            throw new InvalidOperationException(msg);
        }
    }

    static async Task<(string stdout, string stderr, int exitCode)> RunProcessCaptureAsync(
        string workingDir,
        string fileName,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p == null)
            throw new InvalidOperationException($"Не удалось запустить {fileName}");
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (stdout, stderr, p.ExitCode);
    }
}
