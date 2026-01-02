using System.Net;
using System.Net.Sockets;

namespace ClipboardSync.WindowsAgent.Tests;

internal static class TestPort
{
    public static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}


