using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosStream.Models;

namespace SonosStream.ViewModels;

/// <summary>
/// Wraps a <see cref="SonosDevice"/> with UI-binding properties and per-device
/// play/stop commands, delegating control back to <see cref="MainViewModel"/>.
/// </summary>
public partial class DeviceViewModel : ObservableObject
{
    private readonly MainViewModel _mainVm;

    public SonosDevice Device { get; }

    // ── Forwarded properties ───────────────────────────────────────────────────

    public string FriendlyName => Device.FriendlyName;
    public string IpAddress => Device.IpAddress;
    public string ModelName => Device.ModelName;
    public bool IsCoordinator => Device.IsCoordinator;

    // ── Observable state ───────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamButtonLabel))]
    [NotifyPropertyChangedFor(nameof(ToggleButtonLabel))]
    private bool _isBusy;

    /// <summary>Volume 0-100, two-way bound to the per-device slider.</summary>
    [ObservableProperty]
    private int _volume = 50;

    /// <summary>Streaming state text shown in the device info column.</summary>
    public string StreamButtonLabel => IsStreaming ? "● Streaming" : "○ Idle";

    /// <summary>Label shown on the per-device action button.</summary>
    public string ToggleButtonLabel => IsStreaming ? "Stop" : "Play";

    // ── Constructor ────────────────────────────────────────────────────────────

    public DeviceViewModel(SonosDevice device, MainViewModel mainVm)
    {
        Device = device;
        _mainVm = mainVm;
        _volume = device.Volume;
        _isStreaming = device.IsStreaming;
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleStreamAsync()
    {
        if (IsStreaming)
            await _mainVm.StopDeviceAsync(this);
        else
            await _mainVm.StartDeviceAsync(this);
    }

    // Called by the volume Slider ValueChanged (debounced in code-behind)
    public async Task SetVolumeAsync(int volume)
    {
        Volume = volume;
        Device.Volume = volume;
        await _mainVm.SetVolumeAsync(Device, volume);
    }

    partial void OnIsStreamingChanged(bool value)
    {
        Device.IsStreaming = value;
        OnPropertyChanged(nameof(StreamButtonLabel));
        OnPropertyChanged(nameof(ToggleButtonLabel));
    }
}
