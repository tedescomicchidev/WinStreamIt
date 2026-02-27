using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using SonosStream.Models;

namespace SonosStream.Services;

/// <summary>
/// Sends UPnP SOAP commands to Sonos speakers over HTTP.
/// All Sonos control happens on port 1400.
///
/// Key endpoints:
///   AVTransport  → /MediaRenderer/AVTransport/Control
///   RenderingControl → /MediaRenderer/RenderingControl/Control
/// </summary>
public sealed class SonosControlService
{
    private const string AvTransportEndpoint = "/MediaRenderer/AVTransport/Control";
    private const string RenderingControlEndpoint = "/MediaRenderer/RenderingControl/Control";
    private const string AvTransportService = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string RenderingControlService = "urn:schemas-upnp-org:service:RenderingControl:1";

    private readonly HttpClient _http;

    public SonosControlService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    // ── Transport commands ─────────────────────────────────────────────────────

    /// <summary>
    /// Tell the speaker to load the given stream URL.
    /// MUST be called before Play.
    /// </summary>
    public Task SetStreamUriAsync(SonosDevice device, string streamUrl) =>
        SendAvTransportAsync(device, "SetAVTransportURI", $"""
            <InstanceID>0</InstanceID>
            <CurrentURI>{EscapeXml(streamUrl)}</CurrentURI>
            <CurrentURIMetaData></CurrentURIMetaData>
            """);

    /// <summary>Start playback at normal speed.</summary>
    public Task PlayAsync(SonosDevice device) =>
        SendAvTransportAsync(device, "Play", """
            <InstanceID>0</InstanceID>
            <Speed>1</Speed>
            """);

    /// <summary>Stop playback and release the stream URI.</summary>
    public Task StopAsync(SonosDevice device) =>
        SendAvTransportAsync(device, "Stop", """
            <InstanceID>0</InstanceID>
            """);

    /// <summary>Pause playback (Sonos buffers a few seconds before stopping).</summary>
    public Task PauseAsync(SonosDevice device) =>
        SendAvTransportAsync(device, "Pause", """
            <InstanceID>0</InstanceID>
            """);

    /// <summary>
    /// Group <paramref name="slave"/> under <paramref name="master"/>.
    /// Sends x-rincon:{masterId} as the URI to the slave.
    /// </summary>
    public Task GroupWithAsync(SonosDevice slave, SonosDevice master) =>
        SendAvTransportAsync(slave, "SetAVTransportURI", $"""
            <InstanceID>0</InstanceID>
            <CurrentURI>x-rincon:{EscapeXml(master.Id)}</CurrentURI>
            <CurrentURIMetaData></CurrentURIMetaData>
            """);

    /// <summary>Remove a speaker from its group and make it standalone.</summary>
    public Task UngroupAsync(SonosDevice device) =>
        SendAvTransportAsync(device, "BecomeCoordinatorOfStandaloneGroup", """
            <InstanceID>0</InstanceID>
            """);

    /// <summary>Returns the current transport state, e.g. PLAYING, STOPPED, NO_MEDIA_PRESENT.</summary>
    public async Task<string> GetTransportStateAsync(SonosDevice device)
    {
        var response = await SendAvTransportAsync(device, "GetTransportInfo", """
            <InstanceID>0</InstanceID>
            """);

        // Parse <CurrentTransportState> from SOAP response
        try
        {
            var doc = XDocument.Parse(response);
            var stateEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "CurrentTransportState");
            return stateEl?.Value ?? "UNKNOWN";
        }
        catch
        {
            return "UNKNOWN";
        }
    }

    // ── Volume commands ────────────────────────────────────────────────────────

    /// <summary>Set speaker volume 0-100.</summary>
    public Task SetVolumeAsync(SonosDevice device, int volume) =>
        SendRenderingControlAsync(device, "SetVolume", $"""
            <InstanceID>0</InstanceID>
            <Channel>Master</Channel>
            <DesiredVolume>{Math.Clamp(volume, 0, 100)}</DesiredVolume>
            """);

    /// <summary>Read current speaker volume 0-100.</summary>
    public async Task<int> GetVolumeAsync(SonosDevice device)
    {
        var response = await SendRenderingControlAsync(device, "GetVolume", """
            <InstanceID>0</InstanceID>
            <Channel>Master</Channel>
            """);

        try
        {
            var doc = XDocument.Parse(response);
            var volEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "CurrentVolume");
            if (volEl != null && int.TryParse(volEl.Value, out int vol))
                return Math.Clamp(vol, 0, 100);
        }
        catch { }

        return 50;
    }

    // ── SOAP helpers ───────────────────────────────────────────────────────────

    private Task<string> SendAvTransportAsync(SonosDevice device, string action, string body) =>
        SendSoapAsync(device.IpAddress, device.Port, AvTransportEndpoint,
            action, AvTransportService, body);

    private Task<string> SendRenderingControlAsync(SonosDevice device, string action, string body) =>
        SendSoapAsync(device.IpAddress, device.Port, RenderingControlEndpoint,
            action, RenderingControlService, body);

    private async Task<string> SendSoapAsync(
        string ip, int port, string endpoint,
        string action, string serviceType, string body)
    {
        var soapEnvelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"
                        s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:{action} xmlns:u="{serviceType}">
                  {body}
                </u:{action}>
              </s:Body>
            </s:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"http://{ip}:{port}{endpoint}");

        request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
        request.Headers.Add("SOAPAction", $"\"{serviceType}#{action}\"");

        var response = await _http.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
