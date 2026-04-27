using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Services.StartStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeUpSignalRMessages
{
    const string ClientMethodDockerComposeUp = "docker_compose_up";
    const string ServerMethodDockerComposeUpCompleted = "DockerComposeUpCompleted";

    internal static void Register(HubConnection connection, StartStackService startStackService)
    {
        connection.On<DockerComposeUpRequest>(
            ClientMethodDockerComposeUp,
            async request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return;

                try
                {
                    var result = await startStackService.ExecuteAsync(
                        request.Tag,
                        request.AllocatePorts ?? true,
                        CancellationToken.None);

                    await connection.InvokeAsync(
                        ServerMethodDockerComposeUpCompleted,
                        request.Id,
                        result.Ok,
                        result.Error,
                        new { running = result.Running, serviceLinks = result.ServiceLinks });
                }
                catch (Exception ex)
                {
                    await connection.InvokeAsync(
                    ServerMethodDockerComposeUpCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
                }
            });
    }

    private sealed class DockerComposeUpRequest
    {
        public string? Id { get; set; }
        public string? Tag { get; set; }
        public bool AllocatePorts { get; set; }
    }
}
