using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent;

public sealed class AgentWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hostName = Environment.GetEnvironmentVariable("AGENT_HOST_NAME")?.Trim()
                       ?? Environment.MachineName;

        var reconnectSeconds = 5;
        if (int.TryParse(Environment.GetEnvironmentVariable("AGENT_RECONNECT_SECONDS"), out var sec) && sec >= 1)
            reconnectSeconds = sec;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSignalRSessionAsync(hostName, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[agent] ошибка: {ex.Message}");
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(reconnectSeconds), stoppingToken);
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
