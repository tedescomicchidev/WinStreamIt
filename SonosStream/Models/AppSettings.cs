namespace SonosStream.Models;

/// <summary>
/// Persisted application configuration stored as JSON in LocalApplicationData.
/// </summary>
public class AppSettings
{
    /// <summary>TCP port Kestrel listens on for the audio stream.</summary>
    public int ServerPort { get; set; } = 5901;

    /// <summary>Ring-buffer capacity expressed in seconds of 44100/16/stereo audio.</summary>
    public int BufferSizeSeconds { get; set; } = 5;

    /// <summary>Periodically poll speaker state and re-initiate streaming if it stopped.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>How often (seconds) to poll the speaker transport state.</summary>
    public int AutoReconnectIntervalSeconds { get; set; } = 10;

    /// <summary>Inject silence into the buffer when no audio is playing to prevent Sonos disconnect.</summary>
    public bool SilenceInjection { get; set; } = true;

    /// <summary>Device IDs that were streaming when the app was last closed.</summary>
    public List<string> LastUsedDevices { get; set; } = new();

    /// <summary>NAudio device ID to capture from, or null for the system default.</summary>
    public string? AudioDeviceId { get; set; }
}
