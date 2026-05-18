using System.Text.Json;

namespace AutoUpRelease.Api.Agents.Hubs;

internal static class HubSerialization
{
    internal static readonly JsonSerializerOptions TagItemsJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
