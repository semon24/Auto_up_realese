using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent.Commands;

internal static class DeleteDomainsSignalrMessages
{
    const string ClientMethodDeleteDomains = "delete_domains";
    const string ServerMethodDeleteDomainsCompleted = "DeleteDomainsCompleted";

    internal static void Register(HubConnection connection, AppOptions appOptions)
    {
        // Обработчик должен завершиться быстро: долгий compose блокирует цикл приёма SignalR (ping от сервера не обрабатываются → таймаут/разрыв).
        connection.On<DeleteDomainsRequest>(
            ClientMethodDeleteDomains,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда delete_domains: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunDeleteDomainsAsync(connection, request, appOptions);
                return Task.CompletedTask;
            });
    }

    static async Task RunDeleteDomainsAsync(
        HubConnection connection,
        DeleteDomainsRequest request,
        AppOptions appOptions)
    {
        try
        {               
            var tag = request.Tag?.Trim();
            if (string.IsNullOrWhiteSpace(tag))
            {
                await connection.InvokeAsync(ServerMethodDeleteDomainsCompleted, request.Id, false, "Нужен tag", null);
                return;
            }
            var deployProjectsDir = appOptions.ProjectDeploymentPath.Trim();
            var stateFileName = appOptions.StateProjectFileName.Trim();
            var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

            var domain = request.Domain?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(domain))
            {
                await connection.InvokeAsync(ServerMethodDeleteDomainsCompleted, request.Id, false, "Нужен домен", null);
                return;
            }

            await StackStateStore.RemoveServiceDomainAsync(stackStateFile, tag, domain);
            await AgentsStateFileBuilder.BuildAggregatedAgentsStateAsync(
                appOptions.ProjectDeploymentPath.Trim(),
                appOptions.StateProjectFileName.Trim(),
                appOptions.AgentsJsonFilePath.Trim(),
                appOptions.AgentHostName,
                appOptions.ServiceLinkEnvKeys);
            await AgentServicesSnapshotSignalRMessages.SendFromFileAsync(
                connection,
                appOptions.AgentsJsonFilePath.Trim(),
                CancellationToken.None);


            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] delete_domains завершен: id={request.Id}, ok={true}, error={null}");

            await connection.InvokeAsync(
                ServerMethodDeleteDomainsCompleted,
                request.Id,
                true,
                null,
                new { serviceDomain = domain });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] delete_domains ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodDeleteDomainsCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] delete_domains не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class DeleteDomainsRequest
    {
        public string? Id { get; set; }

        public string? Tag { get; set; }

        public string? Domain { get; set; }
    }
}
