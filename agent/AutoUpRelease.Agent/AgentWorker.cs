using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;

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
                using var ws = await AgentWebSocketConnection.ConnectAsync(hostName, stoppingToken);
                Console.WriteLine("[agent] подключено");

                try
                {
                    await AgentWebSocketMessages.RunReceiveLoopAsync(ws, stoppingToken);
                }
                finally
                {
                    if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        try
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "shutdown",
                                CancellationToken.None);
                        }
                        catch
                        {
                        }
                    }
                }
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
}
