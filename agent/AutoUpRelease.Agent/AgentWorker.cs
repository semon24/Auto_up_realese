using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using AutoUpRelease.Agent.Commands;
using AutoUpRelease.Agent.Services.StartStackService;

namespace AutoUpRelease.Agent;

public sealed class AgentWorker : BackgroundService
{
    private readonly AppOptions _appOptions;
    private readonly StartStackService _startStackService;

    public AgentWorker(IOptions<AppOptions> appOptions, StartStackService startStackService)
    {
        _appOptions = appOptions.Value;
        _startStackService = startStackService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = _appOptions.AgentHostName;
        try
        {
            await RunSignalRSessionAsync(hostName, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] ошибка: {ex.Message}");
        }

        Console.WriteLine("[agent] выход");
    }

    async Task RunSignalRSessionAsync(string hostName, CancellationToken stoppingToken)
    {
        var connection = AgentSignalRConnection.Create(hostName);
        var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.Reconnecting += ex =>
        {
            var reason = ex?.Message ?? "без ошибки";
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR reconnecting: {reason}");
            return Task.CompletedTask;
        };

        connection.Reconnected += newConnectionId =>
        {
            Console.WriteLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR reconnected, connectionId={newConnectionId ?? "<null>"}");
            return Task.CompletedTask;
        };

        connection.Closed += ex =>
        {
            if (ex is null)
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR соединение закрыто");
            else
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR соединение закрыто с ошибкой: {ex.Message}");
            disconnectedTcs.TrySetResult();
            return Task.CompletedTask;
        };

        AgentSignalRPasswordMessages.Register(connection, _appOptions);
        DockerComposeUpSignalRMessages.Register(connection, _startStackService);
        try
        {
            await connection.StartAsync(stoppingToken);
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] SignalR подключено: {hostName}");
            await disconnectedTcs.Task.WaitAsync(stoppingToken);
        }
        finally
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
            }
        }
    }
}
