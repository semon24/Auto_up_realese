using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents.Hubs;
using AutoUpRelease.Api.Agents.Json;
using Microsoft.AspNetCore.SignalR;

namespace AutoUpRelease.Api.Agents;

/// <summary>HTTP и WebSocket-ручки, связанные с агентами.</summary>
public static class AgentRoutes
{
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agents/{hostName}/password", async (string hostName, 
        AgentPasswordBody? body, 
        AgentSessionStore sessions, 
        AgentConnectionStatusFile agentsJson, 
        IHubContext<UiHub> uiHubContext,
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

            await UiHub.PublishAgentUpdatedAsync(uiHubContext, agentsJson, hostName, ct);

            return Results.Json(new { ok = true });
        });

        app.MapDelete("/api/agents/{hostName}", async (string hostName, AgentConnectionStatusFile agentsJson, IHubContext<UiHub> uiHubContext, CancellationToken ct) =>
        {
            if (!agentsJson.TryRemoveIfDisconnected(hostName, out var err))
                return Results.Json(new { error = err }, statusCode: 400);
            await UiHub.PublishAgentUpdatedAsync(uiHubContext, agentsJson, hostName, ct);
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/agents/{hostName}/docker-compose-up", async (
            string hostName,
            StartBody? body,
            AgentSessionStore sessions,
            CancellationToken ct) =>
        {
            var tag = body?.Tag?.Trim();
            if (string.IsNullOrEmpty(tag))
                return Results.Json(new { error = "Нужен tag" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeUpWithAgentAsync(
                hostName,
                tag,
                TimeSpan.FromMinutes(15),
                ct);

            if (!ok)
                return Results.Json(new { error = error ?? "Ошибка запуска на агенте" }, statusCode: 400);

            return Results.Json(new { ok = true, payload });
        });

        app.MapPost("/api/agents/{hostName}/docker-compose-down", async (
            string hostName,
            StartBody? body,
            AgentSessionStore sessions,
            CancellationToken ct) =>
        {
            var tag = body?.Tag?.Trim();
            if (string.IsNullOrEmpty(tag))
                return Results.Json(new { error = "Нужен tag" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeDownWithAgentAsync(
                hostName,
                tag,
                TimeSpan.FromMinutes(15),
                ct);

            if (!ok)
                return Results.Json(new { error = error ?? "Ошибка запуска на агенте" }, statusCode: 400);

            return Results.Json(new { ok = true, payload });
        });

        app.MapPost("/api/agents/{hostName}/docker-compose-restart", async (
            string hostName,
            StartBody? body,
            AgentSessionStore sessions,
            CancellationToken ct) =>
        {
            var tag = body?.Tag?.Trim();
            if (string.IsNullOrEmpty(tag))
                return Results.Json(new { error = "Нужен tag" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeRestartWithAgentAsync(
                hostName,
                tag,
                TimeSpan.FromMinutes(15),
                ct);

            if (!ok)
                return Results.Json(new { error = error ?? "Ошибка рестарта на агенте" }, statusCode: 400);

            return Results.Json(new { ok = true, payload });
        });

        app.MapPost("/api/agents/{hostName}/docker-compose-stop", async (
            string hostName,
            StartBody? body,
            AgentSessionStore sessions,
            CancellationToken ct) =>
        {
            var tag = body?.Tag?.Trim();
            if (string.IsNullOrEmpty(tag))
                return Results.Json(new { error = "Нужен tag" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeStopWithAgentAsync(
                hostName,
                tag,
                TimeSpan.FromMinutes(15),
                ct);

            if (!ok)
                return Results.Json(new { error = error ?? "Ошибка остановки на агенте" }, statusCode: 400);

            return Results.Json(new { ok = true, payload });
        });

        return app;
    }
}
