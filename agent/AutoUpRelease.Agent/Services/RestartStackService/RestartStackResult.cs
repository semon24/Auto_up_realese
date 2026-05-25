namespace AutoUpRelease.Agent.Services.RestartStackService;

public sealed class RestartStackResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public object? Payload { get; init; }

    public static RestartStackResult OkResult(object? payload = null) => new()
    {
        Ok = true,
        Payload = payload
    };

    public static RestartStackResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}