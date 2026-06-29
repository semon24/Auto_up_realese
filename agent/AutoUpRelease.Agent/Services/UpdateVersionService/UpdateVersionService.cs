using Microsoft.Extensions.Options;

namespace AutoUpRelease.Agent.Services.UpdateVersionService;

public sealed class UpdateVersionService
{
    readonly AppOptions _options;

    public UpdateVersionService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task<UpdateVersionResult> ExecuteAsync(
        string? rawStackName,
        string? rawVersion,
        CancellationToken ct = default)
    {
        var stackName = rawStackName?.Trim();
        if (string.IsNullOrWhiteSpace(stackName))
            return UpdateVersionResult.Fail("Нужен stackName");

        var version = rawVersion?.Trim();
        if (string.IsNullOrWhiteSpace(version))
            return UpdateVersionResult.Fail("Нужна version");

        var projectDir = ProjectLayoutResolver.GetProjectDir(_options, stackName);
        var envFile = ProjectLayoutResolver.GetEnvFilePath(_options, stackName);
        var stateFile = ProjectLayoutResolver.GetStateFilePath(_options, stackName);

        if (!Directory.Exists(projectDir))
            return UpdateVersionResult.Fail($"Стек '{stackName}' не найден");

        if (!File.Exists(envFile))
            return UpdateVersionResult.Fail($".env для стека '{stackName}' не найден");

        try
        {
            Console.WriteLine(
                $"[update-version] begin stack={stackName} version={version} dir={projectDir} env={envFile}");

            await StackStateStore.SetOperationAsync(
                stateFile,
                stackName,
                operationType: "update_version",
                operationStatus: "in_progress");

            await DockerCompose.LoginAsync(
                _options.RegistryUrl,
                _options.RegistryUser,
                _options.RegistryPassword);

            await EnvFile.WriteTagAsync(envFile, _options.ImageEnvKey, version);
            await StackStateStore.SetVersionAsync(stateFile, stackName, version);

            var composeEnv = await StackStateStore.GetComposeRuntimeEnvAsync(stateFile, stackName);

            await DockerCompose.RunPullAsync(projectDir, stackName: stackName, env: composeEnv);
            await DockerCompose.RunUpDetachedWithDiagnosticsAsync(
                projectDir,
                composeEnv,
                stackName: stackName);

            var servicesState = await DockerCompose.GetServicesStateAsync(
                projectDir,
                stackName: stackName,
                env: composeEnv);

            await StackStateStore.SaveStackServicesStateAsync(stateFile, stackName, servicesState);
            await StackStateStore.SetOperationAsync(
                stateFile,
                stackName,
                operationType: "update_version",
                operationStatus: "success");

            Console.WriteLine($"[update-version] success stack={stackName} version={version}");
            return UpdateVersionResult.OkResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await StackStateStore.SetOperationAsync(
                stateFile,
                stackName,
                operationType: "update_version",
                operationStatus: "failed",
                error: ex.Message);

            Console.Error.WriteLine(
                $"[update-version] failed stack={stackName} version={version} error={ex.Message}");
            return UpdateVersionResult.Fail(ex.Message);
        }
    }
}
