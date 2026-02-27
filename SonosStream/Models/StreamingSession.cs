namespace SonosStream.Models;

/// <summary>
/// Captures the state of an active streaming session for a single Sonos coordinator.
/// </summary>
public class StreamingSession
{
    /// <summary>The Sonos coordinator device receiving the stream URL.</summary>
    public SonosDevice Coordinator { get; set; } = new();

    /// <summary>Devices grouped under the coordinator (not including the coordinator itself).</summary>
    public List<SonosDevice> GroupedDevices { get; set; } = new();

    /// <summary>The HTTP stream URL sent to the coordinator.</summary>
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>UTC time when this session was established.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>All devices involved in this session (coordinator + grouped).</summary>
    public IEnumerable<SonosDevice> AllDevices =>
        GroupedDevices.Prepend(Coordinator);
}
