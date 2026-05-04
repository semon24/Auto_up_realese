using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Services.StartStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeUpSignalRMessages
{
    const string ClientMethodDockerComposeUp = "docker_compose_up";
    const string ServerMethodDockerComposeUpCompleted = "DockerComposeUpCompleted";

    internal static void Register(HubConnection connection, StartStackService startStackService)
    {
        // Обработчик должен завершиться быстро: долгий compose блокирует цикл приёма SignalR (ping от сервера не обрабатываются → таймаут/разрыв).
        connection.On<DockerComposeUpRequest>(
            ClientMethodDockerComposeUp,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_up: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDockerComposeUpAsync(connection, startStackService, request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeUpAsync(
        HubConnection connection,
        StartStackService startStackService,
        DockerComposeUpRequest request)
    {
        try
        {
            var result = await startStackService.ExecuteAsync(
                request.Tag,
                CancellationToken.None);

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_up завершен: id={request.Id}, ok={result.Ok}, error={result.Error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeUpCompleted,
                request.Id,
                result.Ok,
                result.Error,
                new { running = result.Running, serviceLinks = result.ServiceLinks });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_up ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodDockerComposeUpCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_up не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class DockerComposeUpRequest
    {
        public string? Id { get; set; }
        public string? Tag { get; set; }
    }
}
