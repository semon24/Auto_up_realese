using System.Diagnostics;
using System.Text.Json;

namespace AutoUpRelease.Agent;

/// <summary>
/// Полное удаление рабочего стека: compose down, compose-ресурсы, external ресурсы и рабочая папка.
/// Используется и как rollback после неудачного старта, и как штатный teardown при stop/down.
/// </summary>
public static class StackCleanupService
{
    const string DockerCli = "docker";
    const string DockerComposeCli = DockerCli;

    public static async Task CleanupStackAsync(string stackDir)
    {
        if (!Directory.Exists(stackDir))
        {
            Console.WriteLine($"[cleanup] каталог стека отсутствует, пропуск: {stackDir}");
            return;
        }

        Console.WriteLine($"[cleanup] начало очистки стека: {stackDir}");

        // Всегда пытаемся снять проект compose, даже если up оборвался на полпути или нет "running".
        // -v: именованные volumes из compose; --remove-orphans: висящие контейнеры.
        await TryComposeDownAsync(stackDir);
        await RemoveComposeResourcesAsync(stackDir);
        await RemoveExternalResourcesAsync(stackDir);

        Console.WriteLine($"[cleanup] удаление рабочей папки стека: {stackDir}");
        StackWorkspaceManager.DeleteStackWorkspace(stackDir);
        Console.WriteLine($"[cleanup] очистка стека завершена: {stackDir}");
    }

    static async Task TryComposeDownAsync(string stackDir)
    {
        Console.WriteLine(
            $"[cleanup] docker compose down -v --remove-orphans (тома проекта compose удаляются ключом -v)");

        try
        {
            await DockerCompose.RunAsync(stackDir, null, "down", "-v", "--remove-orphans");
            Console.WriteLine("[cleanup] docker compose down выполнен успешно");
        }
        catch (Exception cleanupEx)
        {
            Console.WriteLine($"[cleanup] docker compose down failed: {cleanupEx.Message}");
        }
    }

    static async Task RemoveComposeResourcesAsync(string composeDir, IReadOnlyList<string>? composeFiles = null)
    {
        try
        {
            var volumes = await GetComposeResourcesByKindAsync(composeDir, "volumes", composeFiles);
            var networks = await GetComposeResourcesByKindAsync(composeDir, "networks", composeFiles);

            Console.WriteLine(
                $"[cleanup] ресурсы из docker compose config: volumes={volumes.Count}, networks={networks.Count}");

            foreach (var volume in volumes.OrderBy(v => v, StringComparer.Ordinal))
                await TryRemoveVolumeAsync(volume);

            foreach (var network in networks.OrderBy(n => n, StringComparer.Ordinal))
                await TryRemoveNetworkAsync(network);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[cleanup] ошибка при удалении ресурсов compose config: {ex.Message}");
        }
    }

    static async Task<HashSet<string>> GetComposeResourcesByKindAsync(
        string composeDir,
        string kind,
        IReadOnlyList<string>? composeFiles)
    {
        var resources = new HashSet<string>(StringComparer.Ordinal);

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
        args.Add($"--{kind}");

        var (stdout, stderr, exit) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, args.ToArray());
        if (exit != 0)
        {
            Console.WriteLine($"[cleanup] docker compose config --{kind} failed: {stderr.Trim()}");
            return resources;
        }

        foreach (var raw in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(raw))
                resources.Add(raw.Trim());
        }

        return resources;
    }

    static async Task RemoveExternalResourcesAsync(string composeDir, IReadOnlyList<string>? composeFiles = null)
    {
        try
        {
            var resources = await GetExternalResourceNamesAsync(composeDir, composeFiles);

            var volList = resources.Volumes.OrderBy(v => v, StringComparer.Ordinal).ToList();
            var netList = resources.Networks.OrderBy(n => n, StringComparer.Ordinal).ToList();

            Console.WriteLine(
                $"[cleanup] внешние ресурсы из compose (external): volumes={volList.Count}, networks={netList.Count}");

            foreach (var volume in volList)
                await TryRemoveVolumeAsync(volume);

            foreach (var network in netList)
                await TryRemoveNetworkAsync(network);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[cleanup] ошибка при удалении внешних ресурсов: {ex.Message}");
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

        var (stdout, _, exit) = await RunProcessCaptureAsync(composeDir, DockerComposeCli, args.ToArray());
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

        Console.WriteLine($"[cleanup] docker volume rm \"{volumeName}\"");

        var (_, stderr, exit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "volume", "rm", volumeName);

        if (exit == 0)
            Console.WriteLine($"[cleanup] volume удалён: {volumeName}");
        else
            Console.WriteLine($"[cleanup] volume rm не удалось (code={exit}): {volumeName} — {stderr.Trim()}");
    }

    static async Task TryRemoveNetworkAsync(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            return;

        Console.WriteLine($"[cleanup] docker network rm \"{networkName}\"");

        var (_, stderr, exit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            "network", "rm", networkName);

        if (exit == 0)
            Console.WriteLine($"[cleanup] network удалена: {networkName}");
        else
            Console.WriteLine($"[cleanup] network rm не удалось (code={exit}): {networkName} — {stderr.Trim()}");
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
