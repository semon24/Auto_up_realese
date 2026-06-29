namespace AutoUpRelease.Agent.Services.UpdateVersionService;

public sealed record UpdateVersionResult(bool Ok, string? Error)
{
    public static UpdateVersionResult OkResult() => new(true, null);
    public static UpdateVersionResult Fail(string error) => new(false, error);
}
