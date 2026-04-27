using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private StartStackContext? BuildContext(string? rawTag)
    {
        var tag = rawTag?.Trim();
        if (string.IsNullOrEmpty(tag))
            return null;

        var deployProjectsDir = _options.ProjectDeploymentPath.Trim();
        var folderForCopyDir = _options.CopyFolderForDeployPath.Trim();
        var stateFileName = _options.StateProjectFileName.Trim();
        var stackDir = StackWorkspaceManager.GetStackDir(deployProjectsDir, tag);
        var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, tag);
        var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

        return new StartStackContext(
            tag,
            deployProjectsDir,
            folderForCopyDir,
            stateFileName,
            stackDir,
            stackEnvFile,
            stackStateFile);
    }

    private async Task<string?> EnsureNotRunningAsync(StartStackContext context)
    {
        if (!Directory.Exists(context.StackDir))
            return null;

        var existingInfo = await StackStateStore.GetStackRuntimeInfoAsync(context.StackStateFile, context.Tag);
        if (existingInfo.Running ||
            string.Equals(existingInfo.OperationStatus, "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            return $"Сервис с тегом '{context.Tag}' уже запущен или запускается";
        }

        return null;
    }
}
