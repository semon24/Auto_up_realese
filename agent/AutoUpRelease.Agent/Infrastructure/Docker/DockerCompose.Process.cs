using System.Diagnostics;

namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    static List<string> BuildComposeArgs(IReadOnlyList<string>? composeFiles, params string[] composeSubcommandArgs)
    {
        var all = new List<string> { "compose" };

        if (composeFiles != null)
        {
            foreach (var file in composeFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;

                all.Add("-f");
                all.Add(file);
            }
        }

        all.AddRange(composeSubcommandArgs);
        return all;
    }

    static async Task RunProcessAsync(
        string workingDir,
        string fileName,
        string[] args,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        if (IsDockerComposeCommand(fileName, args))
            Console.WriteLine($"[docker] exec: {FormatCommand(fileName, args)} (cwd={workingDir})");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            CreateNoWindow = true,
        };
        if (env is not null)
        {
            foreach (var kv in env)
                psi.Environment[kv.Key] = kv.Value;
        }
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

        if (IsDockerComposeCommand(fileName, args))
            Console.WriteLine($"[docker] done: {FormatCommand(fileName, args)}");
    }

    static async Task<(string stdout, string stderr, int exitCode)> RunProcessCaptureAsync(
        string workingDir,
        string fileName,
        string[] args,
        IReadOnlyDictionary<string, string>? env = null)
    {
        if (IsDockerComposeCommand(fileName, args))
            Console.WriteLine($"[docker] exec(capture): {FormatCommand(fileName, args)} (cwd={workingDir})");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (env is not null)
        {
            foreach (var kv in env)
                psi.Environment[kv.Key] = kv.Value;
        }

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p == null)
            throw new InvalidOperationException($"Не удалось запустить {fileName}");

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var exitTask = p.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask, exitTask);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (IsDockerComposeCommand(fileName, args))
            Console.WriteLine($"[docker] done(capture): {FormatCommand(fileName, args)} exit={p.ExitCode}");
        return (stdout, stderr, p.ExitCode);
    }

    static bool IsDockerComposeCommand(string fileName, string[] args) =>
        string.Equals(fileName, DockerCli, StringComparison.OrdinalIgnoreCase) &&
        args.Length > 0 &&
        string.Equals(args[0], "compose", StringComparison.OrdinalIgnoreCase);

    static string FormatCommand(string fileName, string[] args) =>
        $"{fileName} {string.Join(" ", args)}";
}
