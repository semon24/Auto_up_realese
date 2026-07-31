namespace AutoUpRelease.Agent.Services.StartStackService;

sealed record StartStackContext(
    string StackName,
    string Version,
    string ?Domain,
    string DeployProjectsDir,
    string FolderForCopyDir,
    string StateFileName,
    string StackDir,
    string StackEnvFile,
    string StackStateFile,
    bool IsSingleProjectWorkspace,
    string? Registry);
