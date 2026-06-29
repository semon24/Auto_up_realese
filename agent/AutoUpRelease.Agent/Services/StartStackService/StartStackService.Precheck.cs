using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private StartStackContext? BuildContext(string? rawStackName, string? rawVersion, string? rawDomain)
    {
        var stackName = rawStackName?.Trim();
        if (string.IsNullOrEmpty(stackName))
            return null;

        var version = rawVersion?.Trim();
        if (string.IsNullOrEmpty(version))
            return null;
        
        var domain = rawDomain?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
            domain = null;
            
        var isSingleProjectWorkspace = _options.IsSingleProjectWorkspaceMode;
        var deployProjectsDir = _options.ProjectDeploymentPath.Trim();
        var folderForCopyDir = _options.CopyFolderForDeployPath.Trim();
        var stateFileName = _options.StateProjectFileName.Trim();
        var stackDir = isSingleProjectWorkspace
            ? deployProjectsDir
            : StackWorkspaceManager.GetStackDir(deployProjectsDir, stackName);
        var stackEnvFile = isSingleProjectWorkspace
            ? Path.Combine(deployProjectsDir, ".env")
            : StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, stackName);
        var stackStateFile = isSingleProjectWorkspace
            ? Path.Combine(deployProjectsDir, stateFileName)
            : StackWorkspaceManager.GetStackStateFile(deployProjectsDir, stackName, stateFileName);

        return new StartStackContext(
            stackName,
            version,
            domain,
            deployProjectsDir,
            folderForCopyDir,
            stateFileName,
            stackDir,
            stackEnvFile,
            stackStateFile,
            isSingleProjectWorkspace);
    }

    private async Task<string?> EnsureNotRunningAsync(StartStackContext context)
    {
        if (!Directory.Exists(context.StackDir))
            return null;

        if (context.IsSingleProjectWorkspace)
        {
            var stackNames = await StackStateStore.GetStackNamesAsync(context.StackStateFile);
            var existingOtherStack = stackNames
                .FirstOrDefault(x => !string.Equals(x, context.StackName, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(existingOtherStack))
                return $"Single-project уже инициализирован стеком '{existingOtherStack}'";
        }

        var existingInfo = await StackStateStore.GetStackRuntimeInfoAsync(context.StackStateFile, context.StackName);
        if (existingInfo.Running ||
            string.Equals(existingInfo.OperationStatus, "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            return $"Сервис с таким именем '{context.StackName}' уже запущен или запускается";
        }

        return null;
    }
}
