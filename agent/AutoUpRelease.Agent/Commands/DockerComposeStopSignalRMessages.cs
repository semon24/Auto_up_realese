using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Services.StopStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeStopSignalRMessages
{
    const string ClientMethodDockerComposeStop = "docker_compose_stop";
    const string ServerMethodDockerComposeStopCompleted = "DockerComposeStopCompleted";

    internal static void Register(HubConnection connection, StopStackService stopStackService)
    {
        connection.On<DockerComposeStopRequest>(
            ClientMethodDockerComposeStop,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_stop: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDockerComposeStopAsync(connection, stopStackService, request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeStopAsync(
        HubConnection connection,
        StopStackService stopStackService,
        DockerComposeStopRequest request)
    {
        try
        {
            var result = await stopStackService.ExecuteAsync(
                request.Tag,
                CancellationToken.None);

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_stop завершен: id={request.Id}, ok={result.Ok}, error={result.Error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeStopCompleted,
                request.Id,
                result.Ok,
                result.Error,
                (object?)null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_stop ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodDockerComposeStopCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_stop не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class DockerComposeStopRequest
    {
        public string? Id { get; set; }
        public string? Tag { get; set; }
    }
}
