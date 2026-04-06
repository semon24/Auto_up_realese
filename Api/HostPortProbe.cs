using System.Net;
using System.Net.Sockets;

namespace AutoUpRelease.Api;

public static class HostPortProbe
{
    /// <summary>Проверяет, можно ли на хосте занять TCP-порт (bind на 0.0.0.0).</summary>
    public static bool IsTcpPortAvailable(int port)
    {
        if (port is < 1 or > 65535) return false;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
