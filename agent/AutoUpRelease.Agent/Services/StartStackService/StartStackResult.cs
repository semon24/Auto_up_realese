public sealed class StartStackResult
{
    public bool Ok { get; init; }
    public bool Running { get; init; }
    public string? Error { get; init; }
    public object? ServiceLinks { get; init; }
    public static StartStackResult OkResult(object? serviceLinks) => new()
    {
        Ok = true,
        Running = true,
        ServiceLinks = serviceLinks
    };
    public static StartStackResult Fail(string error) => new()
    {
        Ok = false,
        Running = false,
        Error = error
    };
}