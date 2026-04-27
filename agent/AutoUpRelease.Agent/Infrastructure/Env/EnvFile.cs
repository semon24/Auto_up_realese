using System.Text.RegularExpressions;

namespace AutoUpRelease.Agent;

public static class EnvFile
{
    public static string? ReadTag(string path, string key)
    {
        try
        {
            var raw = File.ReadAllText(path);
            var re = new Regex(
                @"^\s*" + Regex.Escape(key) + @"\s*=\s*(.+)\s*$",
                RegexOptions.Multiline);
            var m = re.Match(raw);
            if (!m.Success) return null;
            var v = m.Groups[1].Value.Trim();
            if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
                return v[1..^1];
            return v;
        }
        catch
        {
            return null;
        }
    }

    public static Task WriteTagAsync(string path, string key, string tag) =>
        WriteTagsAsync(path, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [key] = tag
        });

    public static async Task WriteTagsAsync(string path, IReadOnlyDictionary<string, string> tags)
    {
        if (tags.Count == 0) return;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(path);
        }
        catch
        {
            raw = "";
        }

        foreach (var kv in tags)
        {
            var key = kv.Key;
            var line = $"{key}={kv.Value}";
            var re = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=.*$", RegexOptions.Multiline);
            if (re.IsMatch(raw))
                raw = re.Replace(raw, line, 1);
            else if (string.IsNullOrWhiteSpace(raw))
                raw = line + Environment.NewLine;
            else
                raw = raw.TrimEnd() + Environment.NewLine + line + Environment.NewLine;
        }

        await File.WriteAllTextAsync(path, raw);
    }
}
