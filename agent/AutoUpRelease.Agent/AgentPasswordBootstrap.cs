using System.Security.Cryptography;
using System.Text.Json;

namespace AutoUpRelease.Agent;

internal static class AgentPasswordBootstrap
{
    public static void WritePassword(string? passwordJsonPath)
    {
        var path = passwordJsonPath?.Trim();
        if (string.IsNullOrEmpty(path))
            return;

        var fullPath = Path.GetFullPath(path);
        var existingPassword = TryReadPasswordFromFile(fullPath);
        if (!string.IsNullOrWhiteSpace(existingPassword) && !IsOldGeneratedPassword(existingPassword))
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

    internal static string? TryReadPassword(string? passwordJsonPath)
    {
        var path = passwordJsonPath?.Trim();
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

    static string? TryReadPasswordFromFile(string fullPath)
    {
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

    static bool IsOldGeneratedPassword(string password)
    {
        try
        {
            return Convert.FromBase64String(password).Length == 32;
        }
        catch
        {
            return false;
        }
    }

    static string GeneratePassword()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return RandomNumberGenerator.GetString(alphabet, 32);
    }
}
