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
        CancellationToken ct = default)
    {
        var context = BuildContext(rawTag);
        if (context is null)
            return StartStackResult.Fail("Нужен tag");

        try
        {
            Console.WriteLine($"[start-stack] этап=begin tag={context.Tag} dir={context.StackDir}");

            Console.WriteLine("[start-stack] этап=precheck");
            var precheckError = await EnsureNotRunningAsync(context);
            if (precheckError is not null)
            {
                Console.WriteLine($"[start-stack] этап=precheck status=failed reason={precheckError}");
                return StartStackResult.Fail(precheckError);
            }

            Console.WriteLine("[start-stack] этап=prepare");
            await PrepareStackAsync(context, ct);
            Console.WriteLine("[start-stack] этап=prepare status=ok");

            Console.WriteLine("[start-stack] этап=allocate_ports");
            var allocationError = await AllocatePortsAsync(context, ct);
            if (allocationError is not null)
            {
                Console.WriteLine($"[start-stack] этап=allocate_ports status=failed reason={allocationError}");
                return await FailAndCleanupAsync(context, allocationError);
            }
            Console.WriteLine("[start-stack] этап=allocate_ports status=ok");

            Console.WriteLine("[start-stack] этап=run_and_wait_ready");
            var waitResult = await RunAndWaitReadyAsync(context, ct);
            if (!waitResult.IsReady)
            {
                Console.WriteLine($"[start-stack] этап=run_and_wait_ready status=failed reason={waitResult.Reason ?? "<unknown>"}");
                return await FailAndCleanupAsync(context, BuildStartFailedMessage(waitResult));
            }
            Console.WriteLine("[start-stack] этап=run_and_wait_ready status=ok");

            Console.WriteLine("[start-stack] этап=complete_success");
            return await CompleteSuccessAsync(context, waitResult.ServicesState);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[start-stack] этап=exception message={ex.Message}");
            return await FailAndCleanupAsync(context, ex.Message);
        }
    }
}

