namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    static async Task EnsureStackNetworkAsync(string tag)
    {
        await EnsureNetworkExistsAsync($"web_auto_release_{tag}");
    }

    public static async Task EnsureNetworkExistsAsync(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            return;

        var (_, _, inspectExit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["network", "inspect", networkName]);

        if (inspectExit == 0)
            return;

        await RunProcessAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["network", "create", networkName]);
    }
}
