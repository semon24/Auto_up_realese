namespace AutoUpRelease.Agent.Services.DeleteStackService;

public sealed class DeleteStackResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public static DeleteStackResult OkResult() => new()
    {
        Ok = true
    };

    public static DeleteStackResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}
