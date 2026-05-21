using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Services.StopStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeDownSignalRMessages
{
    const string ClientMethodDockerComposeDown = "docker_compose_down";
    const string ServerMethodDockerComposeDownCompleted = "DockerComposeDownCompleted";

    internal static void Register(HubConnection connection, StopStackService stopStackService)
    {
        connection.On<DockerComposeDownRequest>(
            ClientMethodDockerComposeDown,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_down: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDockerComposeDownAsync(connection, stopStackService, request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeDownAsync(
        HubConnection connection,
        StopStackService stopStackService,
        DockerComposeDownRequest request)
    {
        try
        {
            var result = await stopStackService.ExecuteAsync(
                request.Tag,
                CancellationToken.None);

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_down завершен: id={request.Id}, ok={result.Ok}, error={result.Error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeDownCompleted,
                request.Id,
                result.Ok,
                result.Error,
                (object?)null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_down ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodDockerComposeDownCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_down не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class DockerComposeDownRequest
    {
        public string? Id { get; set; }
        public string? Tag { get; set; }
    }
}
