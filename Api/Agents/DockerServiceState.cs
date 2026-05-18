namespace AutoUpRelease.Api.Agents;

/// <summary>Состояние одного сервиса в snapshot от агента (docker compose).</summary>
public sealed record DockerServiceState(string State, string? Health);
