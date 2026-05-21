namespace AutoUpRelease.Agent.Services.StopStackService;

public sealed class StopStackResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public static StopStackResult OkResult() => new()
    {
        Ok = true
    };

    public static StopStackResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}
