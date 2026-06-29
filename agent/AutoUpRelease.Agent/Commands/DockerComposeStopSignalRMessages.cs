using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using AutoUpRelease.Agent.Services.SingleProjectControlService;
using AutoUpRelease.Agent.Services.StopStackService;

namespace AutoUpRelease.Agent.Commands;

internal static class DockerComposeStopSignalRMessages
{
    const string ClientMethodDockerComposeStop = "docker_compose_stop";
    const string ServerMethodDockerComposeStopCompleted = "DockerComposeStopCompleted";

    internal static void Register(
        HubConnection connection,
        StopStackService stopStackService,
        SingleProjectControlService singleProjectControlService,
        IOptions<AppOptions> appOptions)
    {
        connection.On<DockerComposeStopRequest>(
            ClientMethodDockerComposeStop,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда docker_compose_stop: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDockerComposeStopAsync(
                    connection,
                    stopStackService,
                    singleProjectControlService,
                    appOptions.Value,
                    request);
                return Task.CompletedTask;
            });
    }

    static async Task RunDockerComposeStopAsync(
        HubConnection connection,
        StopStackService stopStackService,
        SingleProjectControlService singleProjectControlService,
        AppOptions appOptions,
        DockerComposeStopRequest request)
    {
        try
        {
            var (ok, error, payload) = appOptions.IsSingleProjectWorkspaceMode
                ? await ToTupleAsync(singleProjectControlService.StopAsync(CancellationToken.None))
                : await ToTupleAsync(stopStackService.ExecuteAsync(
                    request.Tag,
                    CancellationToken.None));

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] docker_compose_stop завершен: id={request.Id}, ok={ok}, error={error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodDockerComposeStopCompleted,
                request.Id,
                ok,
                error,
                payload);
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

    static async Task<(bool Ok, string? Error, object? Payload)> ToTupleAsync(Task<StopStackResult> task)
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
