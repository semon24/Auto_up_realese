using System.Security.Cryptography;
using System.Text.Json;

namespace AutoUpRelease.Agent;

internal static class AgentPasswordBootstrap
{
    public static void WritePassword()
    {
        var path = Environment.GetEnvironmentVariable("AGENT_PASSWORD_JSON_PATH")?.Trim();
        if (string.IsNullOrEmpty(path))
            return;

        var fullPath = Path.GetFullPath(path);
        if (FileHasNonEmptyPassword(fullPath))
        {
            Console.WriteLine($"[agent] пароль уже есть в {fullPath}, перезапись не выполняется");
            return;
        }

        var password = GeneratePassword();
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var payload = new { password };
        var json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(fullPath, json);
        Console.WriteLine($"[agent] пароль записан в {fullPath}");
    }

    internal static string? TryReadPassword()
    {
        var path = Environment.GetEnvironmentVariable("AGENT_PASSWORD_JSON_PATH")?.Trim();
        if (string.IsNullOrEmpty(path))
            return null;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return null;

        try
        {
            var text = File.ReadAllText(fullPath);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("password", out var el) || el.ValueKind != JsonValueKind.String)
                return null;
            var password = el.GetString();
            return string.IsNullOrWhiteSpace(password) ? null : password;
        }
        catch
        {
            return null;
        }
    }

    static bool FileHasNonEmptyPassword(string fullPath)
    {
        if (!File.Exists(fullPath))
            return false;

        try
        {
            var text = File.ReadAllText(fullPath);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("password", out var el) || el.ValueKind != JsonValueKind.String)
                return false;
            var password = el.GetString();
            return !string.IsNullOrWhiteSpace(password);
        }
        catch
        {
            return false;
        }
    }

    static string GeneratePassword()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
