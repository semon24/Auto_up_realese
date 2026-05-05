using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent.Commands;

internal static class AgentServicesSnapshotSignalRMessages
{
    const string ServerMethodAgentServicesSnapshot = "AgentServicesSnapshot";

    internal static async Task SendFromFileAsync(
        HubConnection connection,
        string aggregatedFilePath,
        CancellationToken cancellationToken)
    {
        if (connection.State != HubConnectionState.Connected)
            return;

        var payload = await ReadPayloadOrEmptyAsync(aggregatedFilePath, cancellationToken);
        await connection.InvokeAsync(
            ServerMethodAgentServicesSnapshot,
            payload,
            cancellationToken);
    }

    static async Task<JsonElement> ReadPayloadOrEmptyAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return EmptyPayload();

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return EmptyPayload();

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch
        {
            return EmptyPayload();
        }
    }

    static JsonElement EmptyPayload()
    {
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            stacks = new Dictionary<string, object>(StringComparer.Ordinal)
        };
        return JsonSerializer.SerializeToElement(payload);
    }
}
