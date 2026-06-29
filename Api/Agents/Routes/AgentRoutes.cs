using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents.Hubs;
using AutoUpRelease.Api.Agents.Json;
using AutoUpRelease.Api.Ssl;
using Microsoft.AspNetCore.SignalR;
using System.Net;

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
            AgentConnectionStatusFile agentsJson,
            CancellationToken ct) =>
        {
            if (TryBuildReadonlyError(hostName, agentsJson) is { } readonlyError)
                return readonlyError;

            var stackName = body?.StackName?.Trim();
            if (string.IsNullOrEmpty(stackName))
                return Results.Json(new { error = "Нужен stackName" }, statusCode: 400);
            
            var version = body?.Version?.Trim();
            if (string.IsNullOrEmpty(version))
                return Results.Json(new { error = "Нужен version" }, statusCode: 400);

            var domain = body?.Domain?.Trim();
            if (string.IsNullOrWhiteSpace(domain) &&
                agentsJson.TryGet(hostName, out var agentInfo))
            {
                domain = NormalizeDomainOrIp(agentInfo?.IpAddress);
            }
            if (string.IsNullOrWhiteSpace(domain))
                return Results.Json(new { error = "Не удалось определить domain" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeUpWithAgentAsync(
                hostName,
                stackName,
                version,
                domain,
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
            AgentConnectionStatusFile agentsJson,
            CancellationToken ct) =>
        {
            if (TryBuildReadonlyError(hostName, agentsJson) is { } readonlyError)
                return readonlyError;

            var stackName = body?.StackName?.Trim();
            if (string.IsNullOrEmpty(stackName))
                return Results.Json(new { error = "Нужен stackName" }, statusCode: 400);


            var (ok, error, payload) = await sessions.StartDockerComposeDownWithAgentAsync(
                hostName,
                stackName,
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
            AgentConnectionStatusFile agentsJson,
            CancellationToken ct) =>
        {
            if (TryBuildReadonlyError(hostName, agentsJson) is { } readonlyError)
                return readonlyError;

            var stackName = body?.StackName?.Trim();
            if (string.IsNullOrEmpty(stackName))
                return Results.Json(new { error = "Нужен stackName" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeRestartWithAgentAsync(
                hostName,
                stackName,
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
            AgentConnectionStatusFile agentsJson,
            CancellationToken ct) =>
        {
            if (TryBuildReadonlyError(hostName, agentsJson) is { } readonlyError)
                return readonlyError;

            var stackName = body?.StackName?.Trim();
            if (string.IsNullOrEmpty(stackName))
                return Results.Json(new { error = "Нужен stackName" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartDockerComposeStopWithAgentAsync(
                hostName,
                stackName,
                TimeSpan.FromMinutes(15),
                ct);

            if (!ok)
                return Results.Json(new { error = error ?? "Ошибка остановки на агенте" }, statusCode: 400);

            return Results.Json(new { ok = true, payload });
        });

        app.MapPost("/api/agents/{hostName}/update-version", async (
            string hostName,
            StartBody? body,
            AgentSessionStore sessions,
            AgentConnectionStatusFile agentsJson,
            CancellationToken ct) =>
        {
            if (TryBuildReadonlyError(hostName, agentsJson) is { } readonlyError)
                return readonlyError;

            var stackName = body?.StackName?.Trim();
            if (string.IsNullOrEmpty(stackName))
                return Results.Json(new { error = "Нужен stackName" }, statusCode: 400);

            var version = body?.Version?.Trim();
            if (string.IsNullOrEmpty(version))
                return Results.Json(new { error = "Нужна version" }, statusCode: 400);

            var (ok, error, payload) = await sessions.StartUpdateVersionWithAgentAsync(
                hostName,
                stackName,
                version,
                TimeSpan.FromMinutes(15),
                ct);

            if (!ok)
                return Results.Json(new { error = error ?? "Ошибка обновления версии на агенте" }, statusCode: 400);

            return Results.Json(new { ok = true, payload });
        });

        return app;
    }

    static IResult? TryBuildReadonlyError(string hostName, AgentConnectionStatusFile agentsJson)
    {
        if (!agentsJson.TryGet(hostName, out var info))
            return null;

        if (!string.Equals(info?.Type, "readonly", StringComparison.OrdinalIgnoreCase))
            return null;

        return Results.Json(
            new { error = $"Агент '{hostName.Trim()}' подключён в режиме readonly. Управляющие действия запрещены." },
            statusCode: 403);
    }

    static string? NormalizeDomainOrIp(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (!IPAddress.TryParse(normalized, out var ip))
            return normalized;

        if (ip.IsIPv4MappedToIPv6)
            return ip.MapToIPv4().ToString();

        return ip.ToString();
    }
}
