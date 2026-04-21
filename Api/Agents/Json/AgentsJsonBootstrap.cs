namespace AutoUpRelease.Api.Agents.Json;

internal static class AgentsJsonBootstrap
{
    /// <summary>Создаёт agents.json с пустым объектом, если файла ещё нет.</summary>
    public static void EnsureExists(string? agentsJsonFullPath)
    {
        if (string.IsNullOrEmpty(agentsJsonFullPath))
            return;
        if (File.Exists(agentsJsonFullPath))
            return;

        var dir = Path.GetDirectoryName(agentsJsonFullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(agentsJsonFullPath, "{}");
    }
}
