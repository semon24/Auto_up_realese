using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using AutoUpRelease.Agent.Services.SingleProjectControlService;
using AutoUpRelease.Agent.Services.StartStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeUpSignalRMessages
{
    const string ClientMethodDockerComposeUp = "docker_compose_up";
    const string ServerMethodDockerComposeUpCompleted = "DockerComposeUpCompleted";

    internal static void Register(
        HubConnection connection,
        StartStackService startStackService,
        SingleProjectControlService singleProjectControlService,
        IOptions<AppOptions> appOptions)
    {
        // Обработчик должен завершиться быстро: долгий compose блокирует цикл приёма SignalR (ping от сервера не обрабатываются → таймаут/разрыв).
        connection.On<DockerComposeUpRequest>(
            ClientMethodDockerComposeUp,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_up: id={request.Id}, StackName={request.StackName?.Trim() ?? "<null>"}, Version={request.Version?.Trim() ?? "<null>"}, Domain={request.Domain?.Trim()}, RegistryChannel={request.RegistryChannel?.Trim() ?? "<null>"}");

                _ = RunDockerComposeUpAsync(
                    connection,
                    startStackService,
                    singleProjectControlService,
                    appOptions.Value,
                    request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeUpAsync(
        HubConnection connection,
        StartStackService startStackService,
        SingleProjectControlService singleProjectControlService,
        AppOptions appOptions,
        DockerComposeUpRequest request)
    {
        try
        {
            var shouldUseSingleProjectControl =
                appOptions.IsSingleProjectMode ||
                (appOptions.IsNewSingleProjectMode && !appOptions.NeedsNewSingleProjectInitialization);

            var (ok, error, payload) = shouldUseSingleProjectControl
                ? await ToTupleAsync(singleProjectControlService.StartAsync(CancellationToken.None))
                : await ToTupleAsync(startStackService.ExecuteAsync(
                    request.StackName,
                    request.Version,
                    request.Domain,
                    request.RegistryChannel,
                    CancellationToken.None));

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_up завершен: id={request.Id}, ok={ok}, error={error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeUpCompleted,
                request.Id,
                ok,
                error,
                payload);
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
        public string? StackName { get; set; }
        public string? Version { get; set; }
        public string? Domain { get; set; }
        public string? RegistryChannel { get; set; }
    }

    static async Task<(bool Ok, string? Error, object? Payload)> ToTupleAsync(Task<StartStackResult> task)
    {
        var result = await task;
        return (result.Ok, result.Error, new { running = result.Running, serviceLinks = result.ServiceLinks });
    }

    static async Task<(bool Ok, string? Error, object? Payload)> ToTupleAsync(Task<SingleProjectControlResult> task)
    {
        var result = await task;
        return (result.Ok, result.Error, result.Payload);
    }
}
