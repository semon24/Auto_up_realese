using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private async Task<(bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState)>
        RunAndWaitReadyAsync(StartStackContext context, CancellationToken ct)
    {
        await EnvFile.WriteTagAsync(context.StackEnvFile, _options.ImageEnvKey, context.Tag);
        await DockerCompose.RunAsync(context.StackDir, null, "up", "-d");

        return await DockerCompose.WaitForServicesReadyAsync(
            context.StackDir,
            timeout: TimeSpan.FromSeconds(600),
            pollInterval: TimeSpan.FromSeconds(2),
            ct);
    }

    private static string BuildStartFailedMessage(
        (bool IsReady, bool HasFailure, string? Reason, Dictionary<string, DockerServiceState> ServicesState) waitResult)
    {
        return waitResult.HasFailure
            ? $"Не удалось успешно запустить стек: {waitResult.Reason}"
            : "Не удалось успешно запустить стек: сервисы не достигли состояния running/exited за отведенное время";
    }
}
