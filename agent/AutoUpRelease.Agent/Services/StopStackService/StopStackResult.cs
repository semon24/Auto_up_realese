namespace AutoUpRelease.Agent.Services.StopStackService;

public sealed class StopStackResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public object? Payload { get; init; }

    public static StopStackResult OkResult(object? payload = null) => new()
    {
        Ok = true,
        Payload = payload
    };

    public static StopStackResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}