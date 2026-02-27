using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using SonosStream.Models;

namespace SonosStream.Services;

/// <summary>
/// Discovers Sonos speakers on the local network using SSDP (UDP multicast M-SEARCH)
/// and then fetches each device's UPnP description XML for full device details.
/// </summary>
public sealed class SonosDiscoveryService : IDisposable
{
    private const string SsdpMulticastAddress = "239.255.255.250";
    private const int SsdpPort = 1900;
    private const string SonosSearchTarget = "urn:schemas-upnp-org:device:ZonePlayer:1";

    private readonly HttpClient _http;
    private System.Threading.Timer? _scanTimer;
    private bool _disposed;

    public ObservableCollection<SonosDevice> Devices { get; } = new();

    public SonosDiscoveryService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task ScanAsync(CancellationToken ct = default)
    {
        var locations = await DiscoverLocationsAsync(ct);
        var tasks = locations.Select(loc => FetchDeviceAsync(loc, ct));
        var devices = await Task.WhenAll(tasks);

        foreach (var device in devices.Where(d => d != null).Cast<SonosDevice>())
        {
            UpdateOrAddDevice(device);
        }

        RemoveStaleDevices(locations);
    }

    public void StartPeriodicScan(TimeSpan interval)
    {
        _scanTimer?.Dispose();
        _scanTimer = new System.Threading.Timer(
            async _ =>
            {
                try { await ScanAsync(); }
                catch { /* best effort */ }
            },
            null,
            interval,
            interval);
    }

    public void StopPeriodicScan()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;
    }

    // ── SSDP M-SEARCH ─────────────────────────────────────────────────────────

    private static async Task<HashSet<string>> DiscoverLocationsAsync(CancellationToken ct)
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchMessage = string.Join("\r\n",
            "M-SEARCH * HTTP/1.1",
            $"HOST: {SsdpMulticastAddress}:{SsdpPort}",
            "MAN: \"ssdp:discover\"",
            "MX: 3",
            $"ST: {SonosSearchTarget}",
            "", "");

        var searchBytes = Encoding.ASCII.GetBytes(searchMessage);
        var multicastEp = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);

        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        await udp.SendAsync(searchBytes, searchBytes.Length, multicastEp);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            while (true)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var response = Encoding.UTF8.GetString(result.Buffer);
                var location = ExtractHeader(response, "LOCATION");
                if (!string.IsNullOrWhiteSpace(location))
                    locations.Add(location);
            }
        }
        catch (OperationCanceledException) { }

        return locations;
    }

    private static string ExtractHeader(string response, string header)
    {
        foreach (var line in response.Split('\n'))
        {
            if (line.StartsWith(header + ":", StringComparison.OrdinalIgnoreCase))
                return line[(header.Length + 1)..].Trim();
        }
        return string.Empty;
    }

    // ── Device description fetch ───────────────────────────────────────────────

    private async Task<SonosDevice?> FetchDeviceAsync(string locationUrl, CancellationToken ct)
    {
        try
        {
            var xml = await _http.GetStringAsync(locationUrl, ct);
            var doc = XDocument.Parse(xml);

            XNamespace ns = "urn:schemas-upnp-org:device-1-0";
            var device = doc.Descendants(ns + "device").FirstOrDefault();
            if (device == null) return null;

            var friendlyName = device.Element(ns + "friendlyName")?.Value ?? string.Empty;
            var modelName = device.Element(ns + "modelName")?.Value ?? string.Empty;
            var udn = device.Element(ns + "UDN")?.Value ?? string.Empty; // uuid:RINCON_XXX
            var roomName = device.Element(ns + "roomName")?.Value ??
                           device.Element(ns + "displayName")?.Value ??
                           friendlyName;

            // Only process Sonos zone players
            if (!udn.Contains("RINCON_", StringComparison.OrdinalIgnoreCase))
                return null;

            var deviceId = udn.Replace("uuid:", string.Empty).Trim();

            if (!Uri.TryCreate(locationUrl, UriKind.Absolute, out var uri))
                return null;

            return new SonosDevice
            {
                Id = deviceId,
                FriendlyName = friendlyName,
                RoomName = roomName,
                ModelName = modelName,
                IpAddress = uri.Host,
                Port = 1400,
                IsCoordinator = true  // Updated by group topology fetch
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Collection management ──────────────────────────────────────────────────

    private void UpdateOrAddDevice(SonosDevice incoming)
    {
        var existing = Devices.FirstOrDefault(d => d.Id == incoming.Id);
        if (existing != null)
        {
            // Update mutable fields without removing/re-adding (preserves streaming state)
            existing.FriendlyName = incoming.FriendlyName;
            existing.ModelName = incoming.ModelName;
            existing.IpAddress = incoming.IpAddress;
        }
        else
        {
            Devices.Add(incoming);
        }
    }

    private void RemoveStaleDevices(HashSet<string> activeLocations)
    {
        // Devices not seen in the latest scan are removed only if not streaming
        // (we rely on URLs containing the device IP)
        var activeIps = activeLocations
            .Where(l => Uri.TryCreate(l, UriKind.Absolute, out _))
            .Select(l => new Uri(l).Host)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            var d = Devices[i];
            if (!activeIps.Contains(d.IpAddress) && !d.IsStreaming)
                Devices.RemoveAt(i);
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPeriodicScan();
        _http.Dispose();
    }
}
