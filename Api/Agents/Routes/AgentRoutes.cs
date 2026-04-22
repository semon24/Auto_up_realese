using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents.Json;

namespace AutoUpRelease.Api.Agents;

/// <summary>HTTP и WebSocket-ручки, связанные с агентами.</summary>
public static class AgentRoutes
{
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        app.Map("/api/agent/ws", async (HttpContext context, AgentSessionStore sessions) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket upgrade required");
                return;
            }

            var hostName = context.Request.Query["hostName"].FirstOrDefault();
            var ws = await context.WebSockets.AcceptWebSocketAsync();
            await sessions.RunAgentWebSocketAsync(hostName, ws, context.RequestAborted);
        });

        app.MapPost("/api/agents/{hostName}/password", async (string hostName, 
        AgentPasswordBody? body, 
        AgentSessionStore sessions, 
        AgentsJsonFile agentsJson, 
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
            return Results.Json(new { ok = true });
        });

        app.MapDelete("/api/agents/{hostName}", (string hostName, AgentsJsonFile agentsJson) =>
        {
            if (!agentsJson.TryRemoveIfDisconnected(hostName, out var err))
                return Results.Json(new { error = err }, statusCode: 400);
            return Results.Json(new { ok = true });
        });

        return app;
    }
}
