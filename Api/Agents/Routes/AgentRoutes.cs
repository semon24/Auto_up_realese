using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents.Hubs;
using AutoUpRelease.Api.Agents.Json;

namespace AutoUpRelease.Api.Agents;

/// <summary>HTTP и WebSocket-ручки, связанные с агентами.</summary>
public static class AgentRoutes
{
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agents/{hostName}/password", async (string hostName, 
        AgentPasswordBody? body, 
        AgentSessionStore sessions, 
        AgentsJsonFile agentsJson, 
        AgentHubPublisher agentHubPublisher,
        CancellationToken ct) =>
        {
            var pwd = body?.Password?.Trim();
            if (string.IsNullOrEmpty(pwd))
                return Results.Json(new { error = "Нужен password" }, statusCode: 400);

            var (agentOk, agentErr) = await sessions.VerifyPasswordWithAgentAsync(
                hostName,
                pwd,
                TimeSpan.FromSeconds(30),
                ct);
            if (!agentOk)
                return Results.Json(new { error = agentErr ?? "Ошибка проверки" }, statusCode: 400);

            if (!agentsJson.TryMarkPasswordAccepted(hostName, out var err))
                return Results.Json(new { error = err }, statusCode: 400);

            await agentHubPublisher.PublishAgentUpdatedAsync(hostName, ct);

            return Results.Json(new { ok = true });
        });

        app.MapDelete("/api/agents/{hostName}", async (string hostName, AgentsJsonFile agentsJson, AgentHubPublisher agentHubPublisher, CancellationToken ct) =>
        {
            if (!agentsJson.TryRemoveIfDisconnected(hostName, out var err))
                return Results.Json(new { error = err }, statusCode: 400);
            await agentHubPublisher.PublishAgentUpdatedAsync(hostName, ct);
            return Results.Json(new { ok = true });
        });

        return app;
    }
}
