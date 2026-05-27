using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent.Commands;

internal static class AddDomainsSignalrMessages
{
    const string ClientMethodAddDomains = "add_domains";
    const string ServerMethodAddDomainsCompleted = "AddDomainsCompleted";

    internal static void Register(HubConnection connection, AppOptions appOptions)
    {
        // Обработчик должен завершиться быстро: долгий compose блокирует цикл приёма SignalR (ping от сервера не обрабатываются → таймаут/разрыв).
        connection.On<AddDomainsRequest>(
            ClientMethodAddDomains,
            request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return Task.CompletedTask;

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда add_domains: id={request.Id}, tag={request.Tag?.Trim() ?? "<null>"}");

                _ = RunAddDomainsAsync(connection, request, appOptions);
                return Task.CompletedTask;
            });
    }

    static async Task RunAddDomainsAsync(
        HubConnection connection,
        AddDomainsRequest request,
        AppOptions appOptions)
    {
        try
        {   
            var domains = request.Domains?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
            
            var tag = request.Tag?.Trim();
            if (string.IsNullOrWhiteSpace(tag))
            {
                await connection.InvokeAsync(ServerMethodAddDomainsCompleted, request.Id, false, "Нужен tag", null);
                return;
            }
            var deployProjectsDir = appOptions.ProjectDeploymentPath.Trim();
            var stateFileName = appOptions.StateProjectFileName.Trim();
            var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

            if (domains is null || domains.Count == 0)
            {
                await connection.InvokeAsync(ServerMethodAddDomainsCompleted, request.Id, false, "Нужен хотя бы один домен", null);
                return;
            }

            await StackStateStore.SetServiceDomainsAsync(stackStateFile, tag, domains);
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
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] add_domains завершен: id={request.Id}, ok={true}, error={null}");

            await connection.InvokeAsync(
                ServerMethodAddDomainsCompleted,
                request.Id,
                true,
                null,
                new { serviceDomains = domains });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] add_domains ошибка: id={request.Id}, message={ex.Message}");

            try
            {
                await connection.InvokeAsync(
                    ServerMethodAddDomainsCompleted,
                    request.Id,
                    false,
                    ex.Message,
                    null);
            }
            catch (Exception invokeEx)
            {
                Console.Error.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] add_domains не смог отправить ответ на сервер: {invokeEx.Message}");
            }
        }
    }

    private sealed class AddDomainsRequest
    {
        public string? Id { get; set; }

        public string? Tag { get; set; }

        public List<string>? Domains { get; set; }
    }
}
