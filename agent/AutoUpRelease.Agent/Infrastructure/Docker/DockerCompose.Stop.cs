namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    public static async Task RunStopAsync(
        string composeDir,
        IReadOnlyList<string>? composeFiles = null)
    {
        var stopArgs = BuildComposeArgs(composeFiles, "stop");
        await RunProcessAsync(composeDir, DockerComposeCli, stopArgs.ToArray());
    }
}
