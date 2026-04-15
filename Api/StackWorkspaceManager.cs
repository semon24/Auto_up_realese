namespace AutoUpRelease.Api;

public static class StackWorkspaceManager
{
    public static string GetStackDir(string deployProjectsDir, string tag) => Path.Combine(deployProjectsDir, tag);

    public static string GetStackEnvFile(string deployProjectsDir, string tag) =>
        Path.Combine(GetStackDir(deployProjectsDir, tag), ".env");

    public static string GetStackStateFile(string deployProjectsDir, string tag, string stateFileName) =>
        Path.Combine(GetStackDir(deployProjectsDir, tag), stateFileName);

    public static void EnsureStackWorkspace(string folderForCopyDir, string stackDir)
    {
        if (Directory.Exists(stackDir))
            return;

        Directory.CreateDirectory(stackDir);
        CopyDirectoryRecursive(folderForCopyDir, stackDir);
    }

    public static void DeleteStackWorkspace(string stackDir)
    {
        if (!Directory.Exists(stackDir))
            return;

        try
        {
            Directory.Delete(stackDir, recursive: true);
        }
        catch (Exception deleteEx)
        {
            Console.WriteLine($"[workspace] stack folder delete failed: {deleteEx.Message}");
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
                File.Copy(file, destFile);
        }

        foreach (var sourceSubDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(targetDir, Path.GetFileName(sourceSubDir));
            CopyDirectoryRecursive(sourceSubDir, destSubDir);
        }
    }
}
