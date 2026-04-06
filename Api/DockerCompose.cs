using System.Diagnostics;

namespace AutoUpRelease.Api;

public static class DockerCompose
{
    const string DockerCli = "docker";

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
            var (stdout, _, exit) = await RunProcessCaptureAsync(
                composeDir,
                DockerCli,
                "compose", "ps", "-q", "--status", "running");
            return exit == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    static async Task RunProcessAsync(string workingDir, string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p == null)
            throw new InvalidOperationException($"Не удалось запустить {fileName}");
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
