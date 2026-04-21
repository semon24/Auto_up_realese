namespace AutoUpRelease.Agent;

/// <summary>Метка последнего прикладного ping от сервера (см. таймаут молчания).</summary>
internal sealed class AgentHeartbeatWatch
{
    readonly object _sync = new();
    DateTime _lastServerPingUtc = DateTime.UtcNow;

    internal void NotifyServerPingReceived()
    {
        lock (_sync)
            _lastServerPingUtc = DateTime.UtcNow;
    }

    internal bool IsSilentLongerThan(TimeSpan maxSilence)
    {
        lock (_sync)
            return DateTime.UtcNow - _lastServerPingUtc >= maxSilence;
    }
}
