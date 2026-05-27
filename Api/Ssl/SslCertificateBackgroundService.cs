using AutoUpRelease.Api.Agents;
using Microsoft.Extensions.Hosting;

namespace AutoUpRelease.Api.Ssl;

public sealed class SslCertificateBackgroundService(
    SslCertificateRefreshService sslCertificateRefreshService) : BackgroundService
{
    static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] SSL background service started: interval={CheckInterval.TotalMinutes:0}m");

        await RunIterationSafeAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunIterationSafeAsync(stoppingToken);
    }

    async Task RunIterationSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (checkedStacks, checkedDomains) = await sslCertificateRefreshService.RefreshAllAsync(cancellationToken);
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] SSL certificates refreshed: stacks={checkedStacks}, domains={checkedDomains}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [api] SSL background service error: {ex.Message}");
        }
    }
}
