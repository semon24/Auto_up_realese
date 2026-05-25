using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Services.RestartStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeRestartSignalRMessages
{
    const string ClientMethodDockerComposeRestart = "docker_compose_restart";
    const string ServerMethodDockerComposeRestartCompleted = "DockerComposeRestartCompleted";

    internal static void Register(HubConnection connection, RestartStackService restartStackService)
    {
        connection.On<DockerComposeRestartRequest>(
            ClientMethodDockerComposeRestart,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_restart: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDockerComposeRestartAsync(connection, restartStackService, request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeRestartAsync(
        HubConnection connection,
        RestartStackService restartStackService,
        DockerComposeRestartRequest request)
    {
        try
        {
            var result = await restartStackService.ExecuteAsync(
                request.Tag,
                CancellationToken.None);

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_restart завершен: id={request.Id}, ok={result.Ok}, error={result.Error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeRestartCompleted,
                request.Id,
                result.Ok,
                result.Error,
                (object?)null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_restart ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodDockerComposeRestartCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_restart не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class DockerComposeRestartRequest
    {
        public string? Id { get; set; }
        public string? Tag { get; set; }
    }
}
