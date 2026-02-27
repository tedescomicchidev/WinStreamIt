using CommunityToolkit.Mvvm.ComponentModel;

namespace SonosStream.Models;

/// <summary>
/// Represents a discovered Sonos speaker on the local network.
/// </summary>
public partial class SonosDevice : ObservableObject
{
    /// <summary>Device ID in RINCON_XXXXXXXXXXXX01400 format.</summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>Display name, e.g. "Living Room".</summary>
    [ObservableProperty]
    private string _friendlyName = string.Empty;

    /// <summary>Room name from Sonos configuration.</summary>
    [ObservableProperty]
    private string _roomName = string.Empty;

    /// <summary>Model name, e.g. "Sonos One".</summary>
    [ObservableProperty]
    private string _modelName = string.Empty;

    /// <summary>LAN IP address of the speaker.</summary>
    [ObservableProperty]
    private string _ipAddress = string.Empty;

    /// <summary>UPnP control port — always 1400 for Sonos.</summary>
    [ObservableProperty]
    private int _port = 1400;

    /// <summary>True if this speaker is the coordinator of its group.</summary>
    [ObservableProperty]
    private bool _isCoordinator;

    /// <summary>Group ID this speaker belongs to.</summary>
    [ObservableProperty]
    private string _groupId = string.Empty;

    // ── Runtime state ──────────────────────────────────────────────────────────

    /// <summary>True when SonosStream is actively pushing audio to this speaker.</summary>
    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>Current volume 0-100 as reported by / sent to the speaker.</summary>
    [ObservableProperty]
    private int _volume = 50;

    public override string ToString() => $"{FriendlyName} ({IpAddress}:{Port})";
}
