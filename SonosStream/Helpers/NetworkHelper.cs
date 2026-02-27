using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SonosStream.Helpers;

/// <summary>
/// Utilities for detecting the machine's LAN IP address.
/// </summary>
public static class NetworkHelper
{
    /// <summary>
    /// Returns the first IPv4 address on an active, non-loopback interface.
    /// Falls back to 127.0.0.1 if nothing is found.
    /// </summary>
    public static string GetLocalIpAddress()
    {
        try
        {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a =>
                    a.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(a.Address));

            return address?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    /// <summary>
    /// Extracts the host (IP or hostname) from a URL string.
    /// </summary>
    public static string GetHostFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;
        return string.Empty;
    }
}
