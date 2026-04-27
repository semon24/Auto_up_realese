using Microsoft.Extensions.Options;
using AutoUpRelease.Agent;

namespace AutoUpRelease.Agent.Services.StartStackService;

public sealed partial class StartStackService
{
    private readonly AppOptions _options;

    public StartStackService(IOptions<AppOptions> options)
    {
        _options = options.Value;
    }

    public async Task<StartStackResult> ExecuteAsync(
        string? rawTag,
        bool allocatePorts,
        CancellationToken ct = default)
    {
        var context = BuildContext(rawTag);
        if (context is null)
            return StartStackResult.Fail("Нужен tag");

        try
        {
            var precheckError = await EnsureNotRunningAsync(context);
            if (precheckError is not null)
                return StartStackResult.Fail(precheckError);

            await PrepareStackAsync(context, ct);

            var allocationError = await AllocatePortsIfNeededAsync(context, allocatePorts, ct);
            if (allocationError is not null)
                return StartStackResult.Fail(allocationError);

            var waitResult = await RunAndWaitReadyAsync(context, ct);
            if (!waitResult.IsReady)
                return await FailAndCleanupAsync(context, BuildStartFailedMessage(waitResult));

            return await CompleteSuccessAsync(context, waitResult.ServicesState);
        }
        catch (Exception ex)
        {
            return await FailAndCleanupAsync(context, ex.Message);
        }
    }
}

