namespace AutoUpRelease.Api;

public static class EnvLoader
{
    // Поменяйте имя, если у вас папка проекта называется не Auto_up_realese, а как-то иначе, то впишите верное название папки. Это нужно для того, чтобы при запуске из папки Api, он смог найти корневой каталог проекта и загрузить .env из папки deploy_auto_up_release (Для локальной разработки).

    public static string RootFolderName { get; set; } = "Auto_up_realese";
    public static string? ProjectRoot { get; private set; }

    public static void LoadOptionalEnvFiles()
    {
        var envPath = FindEnvFile();

        if (envPath is null)
        {
            Console.WriteLine("Для локальной разработки [EnvLoader] Файл .env не найден.");
            return;
        }

        Console.WriteLine($" Для локальной разработки [EnvLoader] Загружен файл: {Path.GetFullPath(envPath)}");

        foreach (var line in File.ReadLines(envPath))
        {
            var t = line.Trim();

            if (t.Length == 0 || t.StartsWith("#", StringComparison.Ordinal))
                continue;

            var eq = t.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = t[..eq].Trim();
            var val = t[(eq + 1)..].Trim();

            if (val.Length >= 2 &&
                ((val[0] == '"' && val[^1] == '"') ||
                 (val[0] == '\'' && val[^1] == '\'')))
            {
                val = val[1..^1];
            }

            if (key.Length > 0)
                Environment.SetEnvironmentVariable(key, val);
        }
    }

    public static string? FindEnvFile()
    {
        var dir = Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(dir))
        {
            var isProjectRoot = string.Equals(
                new DirectoryInfo(dir).Name,
                RootFolderName,
                StringComparison.OrdinalIgnoreCase);

            if (isProjectRoot)
            {
                ProjectRoot = dir;
                var envPath = Path.Combine(dir, "deploy_auto_up_release", ".env");
                return File.Exists(envPath) ? envPath : null;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
    
}
