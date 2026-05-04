using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private async Task<(bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState)>
        RunAndWaitReadyAsync(StartStackContext context, CancellationToken ct)
    {
        Console.WriteLine($"[start-stack] этап=write_tag file={context.StackEnvFile}");
        await EnvFile.WriteTagAsync(context.StackEnvFile, _options.ImageEnvKey, context.Tag);

        Console.WriteLine($"[start-stack] этап=docker_compose_up dir={context.StackDir}");
        await DockerCompose.RunUpDetachedWithDiagnosticsAsync(context.StackDir);

        Console.WriteLine("[start-stack] этап=wait_services_ready timeout_sec=600 poll_sec=2");
        var waitResult = await DockerCompose.WaitForServicesReadyAsync(
            context.StackDir,
            timeout: TimeSpan.FromSeconds(600),
            pollInterval: TimeSpan.FromSeconds(2),
            ct);

        Console.WriteLine(
            $"[start-stack] этап=wait_services_ready finished isReady={waitResult.IsReady} hasFailure={waitResult.HasFailure} reason={waitResult.Reason ?? "<null>"}");

        return waitResult;
    }

    private static string BuildStartFailedMessage(
        (bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState) waitResult)
    {
        return waitResult.HasFailure
            ? $"Не удалось успешно запустить стек: {waitResult.Reason}"
            : "Не удалось успешно запустить стек: сервисы не достигли состояния running/exited за отведенное время";
    }
}
