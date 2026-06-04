namespace AutoUpRelease.Agent.Services.SingleProjectControlService;

public sealed class SingleProjectControlResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public object? Payload { get; init; }

    public static SingleProjectControlResult OkResult(object? payload = null) => new()
    {
        Ok = true,
        Payload = payload
    };

    public static SingleProjectControlResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}
