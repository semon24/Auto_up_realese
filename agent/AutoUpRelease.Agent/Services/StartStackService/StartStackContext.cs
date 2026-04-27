namespace AutoUpRelease.Agent.Services.StartStackService;

sealed record StartStackContext(
    string Tag,
    string DeployProjectsDir,
    string FolderForCopyDir,
    string StateFileName,
    string StackDir,
    string StackEnvFile,
    string StackStateFile);
