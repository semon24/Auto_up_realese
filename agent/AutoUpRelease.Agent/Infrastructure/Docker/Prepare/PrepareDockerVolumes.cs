namespace AutoUpRelease.Agent;

public static partial class DockerCompose
{
    static async Task EnsureCommonVolumesAsync(string tag)
    {
        await EnsureVolumeExistsAsync($"rabbit_data_auto_release_{tag}");
    }

    public static async Task<bool> VolumeExistsAsync(string volumeName)
    {
        if (string.IsNullOrWhiteSpace(volumeName))
            return false;

        var (_, _, inspectExit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["volume", "inspect", volumeName]);

        return inspectExit == 0;
    }

    public static async Task EnsureVolumeExistsAsync(string volumeName)
    {
        if (string.IsNullOrWhiteSpace(volumeName))
            return;

        var (_, _, inspectExit) = await RunProcessCaptureAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["volume", "inspect", volumeName]);

        if (inspectExit == 0)
            return;

        await RunProcessAsync(
            Directory.GetCurrentDirectory(),
            DockerCli,
            ["volume", "create", volumeName]);
    }
}
