using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Commands;

namespace AutoUpRelease.Agent;

public sealed class AgentWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppOptions _appOptions;

    public AgentWorker(IHttpClientFactory httpClientFactory, AppOptions appOptions)
    {
        _httpClientFactory = httpClientFactory;
        _appOptions = appOptions;
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

    static async Task RunSignalRSessionAsync(string hostName, CancellationToken stoppingToken)
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

        AgentSignalRPasswordMessages.Register(connection);
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
