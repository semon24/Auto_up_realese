using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using AutoUpRelease.Agent.Services.SingleProjectControlService;
using AutoUpRelease.Agent.Services.RestartStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeRestartSignalRMessages
{
    const string ClientMethodDockerComposeRestart = "docker_compose_restart";
    const string ServerMethodDockerComposeRestartCompleted = "DockerComposeRestartCompleted";

    internal static void Register(
        HubConnection connection,
        RestartStackService restartStackService,
        SingleProjectControlService singleProjectControlService,
        IOptions<AppOptions> appOptions)
    {
        connection.On<DockerComposeRestartRequest>(
            ClientMethodDockerComposeRestart,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_restart: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDockerComposeRestartAsync(
                    connection,
                    restartStackService,
                    singleProjectControlService,
                    appOptions.Value,
                    request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeRestartAsync(
        HubConnection connection,
        RestartStackService restartStackService,
        SingleProjectControlService singleProjectControlService,
        AppOptions appOptions,
        DockerComposeRestartRequest request)
    {
        try
        {
            var (ok, error, payload) = appOptions.IsSingleProjectWorkspaceMode
                ? await ToTupleAsync(singleProjectControlService.RestartAsync(CancellationToken.None))
                : await ToTupleAsync(restartStackService.ExecuteAsync(
                    request.Tag,
                    CancellationToken.None));

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_restart завершен: id={request.Id}, ok={ok}, error={error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeRestartCompleted,
                request.Id,
                ok,
                error,
                payload);
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

    static async Task<(bool Ok, string? Error, object? Payload)> ToTupleAsync(Task<RestartStackResult> task)
    {
        var result = await task;
        return (result.Ok, result.Error, result.Payload);
    }

    static async Task<(bool Ok, string? Error, object? Payload)> ToTupleAsync(Task<SingleProjectControlResult> task)
    {
        var result = await task;
        return (result.Ok, result.Error, result.Payload);
    }
}
