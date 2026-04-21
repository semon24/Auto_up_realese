using System.Collections.ObjectModel;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents.Json;

/// <summary>Потокобезопасное обновление agents.json: hostName → статус.</summary>
public sealed class AgentsJsonFile
{
    public const string StatusWaitingForPassword = "waiting for password";
    public const string StatusPasswordAccepted = "password accepted";

    readonly string? _path;
    readonly object _lock = new();

    public AgentsJsonFile(string? agentsJsonFullPath) => _path = agentsJsonFullPath;

    /// <summary>Текущее содержимое файла (копия), без пути — пустой словарь.</summary>
    public IReadOnlyDictionary<string, string> ReadSnapshot()
    {
        if (_path is null)
            return ReadOnlyDictionary<string, string>.Empty;
        lock (_lock)
        {
            var map = ReadMap(_path);
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(map, StringComparer.Ordinal));
        }
    }

    public void UpsertWaiting(string hostName)
    {
        if (_path is null) return;
        lock (_lock)
        {
            var map = ReadMap(_path);
            map[hostName] = StatusWaitingForPassword;
            WriteMap(_path, map);
        }
    }

    /// <summary>Удаляет хост из файла (в т.ч. при закрытии WebSocket агента).</summary>
    public void Remove(string hostName)
    {
        if (_path is null) return;
        lock (_lock)
        {
            var map = ReadMap(_path);
            if (!map.Remove(hostName))
                return;
            WriteMap(_path, map);
        }
    }

    /// <summary>
    /// После успешной проверки пароля на агенте: если статус «waiting for password», выставляет «password accepted».
    /// </summary>
    public bool TryMarkPasswordAccepted(string hostName, out string? error)
    {
        error = null;
        if (_path is null)
        {
            error = "AGENTS_JSON_PATH не задан";
            return false;
        }

        var key = hostName.Trim();
        if (string.IsNullOrEmpty(key))
        {
            error = "hostName пустой";
            return false;
        }

        lock (_lock)
        {
            var map = ReadMap(_path);
            if (!map.TryGetValue(key, out var st))
            {
                error = "Агент не найден";
                return false;
            }

            if (!string.Equals(st, StatusWaitingForPassword, StringComparison.Ordinal))
            {
                error = "Пароль уже введён или статус другой";
                return false;
            }

            map[key] = StatusPasswordAccepted;
            WriteMap(_path, map);
        }

        return true;
    }

    static Dictionary<string, string> ReadMap(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        var text = File.ReadAllText(path).Trim();
        if (text.Length == 0 || text[0] == '[')
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(text)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    static void WriteMap(string path, Dictionary<string, string> map)
    {
        var json = JsonSerializer.Serialize(
            map,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
