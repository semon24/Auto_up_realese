namespace AutoUpRelease.Agent;

public static class ProjectLayoutResolver
{
    public static string GetProjectDir(AppOptions options, string stackName)
    {
        var deployPath = options.ProjectDeploymentPath.Trim();

        if (options.IsSingleProjectWorkspaceMode)
            return deployPath;

        return Path.Combine(deployPath, stackName);
    }

    public static string GetEnvFilePath(AppOptions options, string stackName)
    {
        return Path.Combine(GetProjectDir(options, stackName), ".env");
    }

    public static string GetStateFilePath(AppOptions options, string stackName)
    {
        return Path.Combine(
            GetProjectDir(options, stackName),
            options.StateProjectFileName.Trim());
    }

    public static string GetSingleProjectName(AppOptions options)
    {
        var configured = options.StackSingleName?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "singleProject";
    }
}
