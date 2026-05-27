using System.Collections.ObjectModel;
using System.Text.Json;

namespace AutoUpRelease.Api.Agents.Json;

public sealed class AgentConnectionStatusFile
{
    public const string StatusWaitingForPassword = "waiting for password";
    public const string StatusPasswordAccepted = "password accepted";
    public const string StatusDisconnected = "disconnected";

    readonly string? _path;
    readonly object _lock = new();

    public AgentConnectionStatusFile(string? agentsJsonFullPath) => _path = agentsJsonFullPath;

    /// <summary>Текущее содержимое файла (копия), без пути — пустой словарь.</summary>
    public IReadOnlyDictionary<string, AgentConnectionInfo> ReadSnapshot()
    {
        if (_path is null)
            return ReadOnlyDictionary<string, AgentConnectionInfo>.Empty;
        lock (_lock)
        {
            var map = ReadMap(_path);
            return new ReadOnlyDictionary<string, AgentConnectionInfo>(
                new Dictionary<string, AgentConnectionInfo>(map, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Агент открыл WebSocket: новый хост или после «waiting for password» → «waiting for password»;
    /// после «password accepted» / «disconnected» → сразу «password accepted» (повторный ввод пароля не нужен).
    /// </summary>
    public void UpdateStatusOnConnect(string hostName, string? ipAddress, string? agentType)
    {
        if (_path is null) return;

        lock (_lock)
        {
            var map = ReadMap(_path);
            var normalizedType = NormalizeAgentType(agentType);

            if (map.TryGetValue(hostName, out var st))
            {
                st.Status =
                    string.Equals(st.Status, StatusDisconnected, StringComparison.Ordinal) ||
                    string.Equals(st.Status, StatusPasswordAccepted, StringComparison.Ordinal)
                        ? StatusPasswordAccepted
                        : StatusWaitingForPassword;

                st.IpAddress = ipAddress;
                st.Type = normalizedType;
                st.DisconnectedAtUtc = null;
            }
            else
            {
                map[hostName] = new AgentConnectionInfo
                {
                    Status = StatusWaitingForPassword,
                    Type = normalizedType,
                    IpAddress = ipAddress,
                    DisconnectedAtUtc = null
                };
            }

            WriteMap(_path, map);
        }
    }
    /// <summary>
    /// Закрытие WebSocket: при «password accepted» → «disconnected», иначе запись удаляется (ещё не вводили пароль и т.п.).
    /// </summary>
    public void OnAgentWebSocketClosed(string hostName)
    {
        if (_path is null) return;
        lock (_lock)
        {
            var map = ReadMap(_path);
            if (!map.TryGetValue(hostName, out var st))
                return;
            if (string.Equals(st.Status, StatusPasswordAccepted, StringComparison.Ordinal))
            {
                st.Status = StatusDisconnected;
                st.DisconnectedAtUtc = DateTimeOffset.UtcNow;
            }
                
            else
                map.Remove(hostName);
            WriteMap(_path, map);
        }
    }

    /// <summary>Удаляет хост из файла (ручная очистка и т.д.).</summary>
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
    /// Удаляет запись только в статусе <see cref="StatusDisconnected"/> (как «никогда не подключался»).
    /// </summary>
    public bool TryRemoveIfDisconnected(string hostName, out string? error)
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

            if (!string.Equals(st.Status, StatusDisconnected, StringComparison.Ordinal))
            {
                error = "Удалить можно только агента в статусе disconnected";
                return false;
            }

            map.Remove(key);
            WriteMap(_path, map);
        }

        return true;
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

            if (!string.Equals(st.Status, StatusWaitingForPassword, StringComparison.Ordinal))
            {
                error = "Пароль уже введён или статус другой";
                return false;
            }

            map[key].Status = StatusPasswordAccepted;
            WriteMap(_path, map);
        }

        return true;
    }

    public bool TryGet(string hostName, out AgentConnectionInfo? info)
    {
        info = null;
        if (_path is null)
            return false;

        var key = hostName.Trim();
        if (string.IsNullOrEmpty(key))
            return false;

        lock (_lock)
        {
            var map = ReadMap(_path);
            if (!map.TryGetValue(key, out var entry))
                return false;

            info = new AgentConnectionInfo
            {
                Status = entry.Status,
                Type = entry.Type,
                IpAddress = entry.IpAddress,
                DisconnectedAtUtc = entry.DisconnectedAtUtc,
            };
            return true;
        }
    }

    static Dictionary<string, AgentConnectionInfo> ReadMap(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, AgentConnectionInfo>(StringComparer.Ordinal);
        var text = File.ReadAllText(path).Trim();
        if (text.Length == 0 || text[0] == '[')
            return new Dictionary<string, AgentConnectionInfo>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, AgentConnectionInfo>>(text)
                   ?? new Dictionary<string, AgentConnectionInfo>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, AgentConnectionInfo>(StringComparer.Ordinal);
        }
    }

    static void WriteMap(string path, Dictionary<string, AgentConnectionInfo> map)
    {
        var json = JsonSerializer.Serialize(
            map,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    static string NormalizeAgentType(string? agentType)
    {
        var normalized = agentType?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized.ToLowerInvariant();
    }
}
