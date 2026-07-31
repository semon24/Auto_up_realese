using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private async Task<(bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState)>
        RunAndWaitReadyAsync(StartStackContext context, CancellationToken ct)
    {
        Console.WriteLine($"[start-stack] этап=write_tag file={context.StackEnvFile}");
        var tags = await BuildDnsTagsAsync(context);
        await EnvFile.WriteTagsAsync(context.StackEnvFile, tags);
        Console.WriteLine($"[start-stack] этап=docker_compose_up dir={context.StackDir}");

        var composeEnv = await StackStateStore.GetComposeRuntimeEnvAsync(
            context.StackStateFile,
            context.StackName);

        await DockerCompose.RunUpDetachedWithDiagnosticsAsync(
            context.StackDir,
            composeEnv,
            onProgress: servicesState => SaveProgressStateAsync(context, servicesState),
            progressPollInterval: TimeSpan.FromSeconds(2),
            stackName: context.StackName);

        Console.WriteLine("[start-stack] этап=wait_services_ready timeout_sec=600 poll_sec=2");
        var waitResult = await DockerCompose.WaitForServicesReadyAsync(
            context.StackDir,
            timeout: TimeSpan.FromSeconds(600),
            pollInterval: TimeSpan.FromSeconds(2),
            ct,
            onProgress: servicesState => SaveProgressStateAsync(context, servicesState),
            stackName: context.StackName,
            env: composeEnv);

        Console.WriteLine(
            $"[start-stack] этап=wait_services_ready finished isReady={waitResult.IsReady} hasFailure={waitResult.HasFailure} reason={waitResult.Reason ?? "<null>"}");

        return waitResult;
    }

    async Task SaveProgressStateAsync(
        StartStackContext context,
        Dictionary<string, DockerServiceState> servicesState)
    {
        await StackStateStore.SaveStackServicesStateAsync(
            context.StackStateFile,
            context.StackName,
            servicesState);
        await StackStateStore.SetOperationAsync(
            context.StackStateFile,
            context.StackName,
            operationType: "start",
            operationStatus: "in_progress");
    }

    private async Task<Dictionary<string, string>> BuildDnsTagsAsync(StartStackContext context)
    {
        var adminService = EnvFile.ReadTag(context.StackEnvFile, "ADMIN_SERVICE");
        var serverService = EnvFile.ReadTag(context.StackEnvFile, "SERVER_SERVICE");
        var portalService = EnvFile.ReadTag(context.StackEnvFile, "PORTAL_SERVICE");
        var callService = EnvFile.ReadTag(context.StackEnvFile, "CALL_SERVICE");
        var domain = context.Domain?.Trim();
        var ports = await StackStateStore.GetAllocatedPortsAsync(
                        context.StackStateFile,
                        context.StackName);
        var isIp = !string.IsNullOrWhiteSpace(domain) && System.Net.IPAddress.TryParse(domain, out _);
        var DNS_NAME_BACKOFFICE = "";
        var DNS_NAME_SERVER = "";
        var DNS_NAME_CALL ="";
        var DNS_NAME_RDV = "";
        if (!isIp)
        {
            DNS_NAME_BACKOFFICE=$"https://{adminService}.{domain}";
            DNS_NAME_SERVER=$"https://{serverService}.{domain}";
            DNS_NAME_CALL=$"https://{callService}.{domain}";
            DNS_NAME_RDV=$"https://{portalService}.{domain}";
        }
        else
        {
            DNS_NAME_BACKOFFICE=$"http://{domain}:{ports["ADMIN_PORT"]}";
            DNS_NAME_SERVER=$"http://{domain}:{ports["SERVER_PORT"]}";
            DNS_NAME_RDV=$"http://{domain}:{ports["PORTAL_PORT"]}";
            DNS_NAME_CALL=$"http://{domain}:{ports["CALL_PORT"]}";
        }
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [_options.ImageEnvKey] = context.Version,
            ["DOMAIN"] = context.Domain!,
            ["DNS_NAME_BACKOFFICE"] = DNS_NAME_BACKOFFICE,
            ["DNS_NAME_SERVER"] = DNS_NAME_SERVER,
            ["DNS_NAME_RDV"] = DNS_NAME_RDV,
            ["DNS_NAME_CALL"] = DNS_NAME_CALL
        };
    }


    private static string BuildStartFailedMessage(
        (bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState) waitResult)
    {
        return waitResult.HasFailure
            ? $"Не удалось успешно запустить стек: {waitResult.Reason}"
            : "Не удалось успешно запустить стек: сервисы не достигли состояния running/exited за отведенное время";
    }
}
