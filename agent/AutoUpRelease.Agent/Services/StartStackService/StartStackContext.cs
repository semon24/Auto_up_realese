namespace AutoUpRelease.Agent.Services.StartStackService;

sealed record StartStackContext(
    string StackName,
    string Version,
    string DeployProjectsDir,
    string FolderForCopyDir,
    string StateFileName,
    string StackDir,
    string StackEnvFile,
    string StackStateFile);
