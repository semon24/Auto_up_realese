namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    public static async Task RunStopAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null,
        string? stackName = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var stopArgs = BuildComposeArgs(composeFiles, "stop");
        await RunProcessAsync(composeDir, DockerComposeCli, stopArgs.ToArray(), env: env ?? GetComposeEnv(stackName));
    }
}
