namespace AutoUpRelease.Api;

public static class EnvLoader
{
    public static void LoadOptionalEnvFiles()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")), ".env"),
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            Console.WriteLine($"[EnvLoader] Загружен файл: {Path.GetFullPath(path)}");
            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#", StringComparison.Ordinal)) continue;
                var eq = t.IndexOf('=');
                if (eq <= 0) continue;
                var key = t[..eq].Trim();
                var val = t[(eq + 1)..].Trim();
                if (val.Length >= 2 &&
                    ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                    val = val[1..^1];
                if (key.Length > 0)
                    Environment.SetEnvironmentVariable(key, val);
            }
            return;
        }

        Console.WriteLine(
            "[EnvLoader] Файл .env не найден. Искали: {0} и {1}",
            Path.GetFullPath(candidates[0]),
            Path.GetFullPath(candidates[1]));
    }
}
