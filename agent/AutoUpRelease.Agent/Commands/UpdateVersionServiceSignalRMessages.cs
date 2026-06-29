using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Services.UpdateVersionService;

namespace AutoUpRelease.Agent.Commands;

internal static class UpdateVersionServiceSignalRMessages
{
    const string ClientMethodUpdateVersion = "update_version";
    const string ServerMethodUpdateVersionCompleted = "UpdateVersionCompleted";

    internal static void Register(HubConnection connection, UpdateVersionService updateVersionService)
    {
        connection.On<UpdateVersionRequest>(
            ClientMethodUpdateVersion,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда update_version: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}, version={request.Version?.Trim() ?? "<null>"}");

                _ = RunUpdateVersionAsync(connection, updateVersionService, request);
                return Task.CompletedTask;
            });
    }

    static async Task RunUpdateVersionAsync(
        HubConnection connection,
        UpdateVersionService updateVersionService,
        UpdateVersionRequest request)
    {
        try
        {
            

            var result = await updateVersionService.ExecuteAsync(
                request.Tag,
                request.Version,
                CancellationToken.None);

            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] update_version завершен: id={request.Id}, ok={result.Ok}, error={result.Error ?? "<null>"}");

            await connection.InvokeAsync(
                ServerMethodUpdateVersionCompleted,
                request.Id,
                result.Ok,
                result.Error,
                (object?)null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] update_version ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodUpdateVersionCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] update_version не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class UpdateVersionRequest
    {
        public string? Id { get; set; }
        public string? Tag { get; set; }
        public string? Version { get; set; }
    }
}
