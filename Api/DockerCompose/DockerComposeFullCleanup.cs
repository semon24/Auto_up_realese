using System.Text.Json;
using System.Diagnostics;

namespace AutoUpRelease.Api;

public static class DockerComposeFullCleanup
{
    const string DockerCli = "docker";

    public static async Task CleanupStackAsync(string stackDir)
    {
        if (!Directory.Exists(stackDir))
            return;

        await TryComposeDownAsync(stackDir);
        await RemoveExternalResourcesAsync(stackDir);
        StackWorkspaceManager.DeleteStackWorkspace(stackDir);
    }

    static async Task TryComposeDownAsync(string stackDir)
    {
        try
        {
            // Всегда пытаемся снять проект compose, даже если сервисы не в "running".
            // -v + --remove-orphans: очищаем volume и orphan-контейнеры проекта compose.
            await DockerCompose.RunAsync(stackDir, null, "down", "-v", "--remove-orphans");
        }
        catch (Exception cleanupEx)
        {
            Console.WriteLine($"[cleanup] docker compose down failed: {cleanupEx.Message}");
        }
    }

    static async Task RemoveExternalResourcesAsync(string composeDir, IReadOnlyList<string>? composeFiles = null)
    {
        try
        {
            var resources = await GetExternalResourceNamesAsync(composeDir, composeFiles);

            foreach (var volume in resources.Volumes)
                await TryRemoveVolumeAsync(volume);

            foreach (var network in resources.Networks)
                await TryRemoveNetworkAsync(network);
        }
        catch
        {
            // Cleanup path should be best-effort: ignore parse/runtime failures.
        }
    }

    static async Task<(HashSet<string> Volumes, HashSet<string> Networks)> GetExternalResourceNamesAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles)
    {
        var volumes = new HashSet<string>(StringComparer.Ordinal);
        var networks = new HashSet<string>(StringComparer.Ordinal);

        var args = new List<string> { "compose" };
        if (composeFiles != null)
        {
            foreach (var file in composeFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;

                args.Add("-f");
                args.Add(file);
            }
        }
        args.Add("config");
        args.Add("--format");
        args.Add("json");

        var (stdout, _, exit) = await RunProcessCaptureAsync(composeDir, DockerCli, args.ToArray());
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
            return (volumes, networks);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        CollectExternalResourceNames(root, "volumes", volumes);
        CollectExternalResourceNames(root, "networks", networks);

        return (volumes, networks);
    }

    static void CollectExternalResourceNames(JsonElement root, string propertyName, HashSet<string> output)
    {
        if (!root.TryGetProperty(propertyName, out var group) || group.ValueKind != JsonValueKind.Object)
            return;

        foreach (var item in group.EnumerateObject())
        {
            var definition = item.Value;
            if (definition.ValueKind != JsonValueKind.Object || !IsExternal(definition))
                continue;

            var explicitName = TryGetString(definition, "name");
            var resourceName = string.IsNullOrWhiteSpace(explicitName) ? item.Name : explicitName.Trim();
            if (!string.IsNullOrWhiteSpace(resourceName))
                output.Add(resourceName);
        }
    }

    static bool IsExternal(JsonElement definition)
    {
        if (!definition.TryGetProperty("external", out var external))
            return false;

        if (external.ValueKind == JsonValueKind.True)
            return true;

        if (external.ValueKind == JsonValueKind.Object &&
            external.TryGetProperty("name", out var nameElement) &&
            nameElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(nameElement.GetString()))
            return true;

        return false;
    }

    static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    static async Task TryRemoveVolumeAsync(string volumeName)
    {
        if (string.IsNullOrWhiteSpace(volumeName))
            return;

        await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "volume", "rm", volumeName);
    }

    static async Task TryRemoveNetworkAsync(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            return;

        await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "network", "rm", networkName);
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
