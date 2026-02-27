using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SonosStream.Models;
using SonosStream.Services;

namespace SonosStream.ViewModels;

/// <summary>
/// Primary application ViewModel. Coordinates discovery, audio capture,
/// HTTP streaming, SOAP control, and health-check auto-reconnect.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ── Services ───────────────────────────────────────────────────────────────
    private readonly SonosDiscoveryService _discovery;
    private readonly SonosControlService _control;
    private readonly AudioCaptureService _capture;
    private readonly AudioStreamServer _server;
    private readonly SettingsService _settings;

    // ── Threading ──────────────────────────────────────────────────────────────
    private DispatcherQueue? _dispatcherQueue;
    private readonly System.Threading.CancellationTokenSource _appCts = new();
    private DispatcherTimer? _healthCheckTimer;

    // ── Observable properties ──────────────────────────────────────────────────

    [ObservableProperty]
    private string _statusText = "Initializing…";

    [ObservableProperty]
    private bool _isDiscovering;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private int _activeConnections;

    [ObservableProperty]
    private string _streamUrl = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>The list of discovered speakers bound to the ListView.</summary>
    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel(
        SonosDiscoveryService discovery,
        SonosControlService control,
        AudioCaptureService capture,
        AudioStreamServer server,
        SettingsService settings)
    {
        _discovery = discovery;
        _control = control;
        _capture = capture;
        _server = server;
        _settings = settings;

        _discovery.Devices.CollectionChanged += OnDiscoveredDevicesChanged;
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call from the UI thread after the window is shown so we have a DispatcherQueue.
    /// </summary>
    public async Task InitializeAsync(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        // Start health-check timer on UI thread
        _healthCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.Settings.AutoReconnectIntervalSeconds)
        };
        _healthCheckTimer.Tick += async (s, e) => await HealthCheckAsync();
        _healthCheckTimer.Start();

        // Start the Kestrel HTTP server
        try
        {
            await _server.StartAsync(_appCts.Token);
            StreamUrl = _server.StreamUrl;
            StatusText = "Ready — scan for speakers";
        }
        catch (Exception ex)
        {
            SetError($"Failed to start stream server: {ex.Message}");
        }

        // Initial SSDP scan
        await ScanAsync();

        // Start periodic re-scan every 30 s
        _discovery.StartPeriodicScan(TimeSpan.FromSeconds(30));

        // Auto-reconnect last-used devices
        if (_settings.Settings.AutoReconnect && _settings.Settings.LastUsedDevices.Any())
            await AutoReconnectLastDevicesAsync();
    }

    // ── Scanning ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAsync()
    {
        IsDiscovering = true;
        StatusText = "Scanning for Sonos speakers…";
        ClearError();

        try
        {
            await _discovery.ScanAsync(_appCts.Token);
            StatusText = Devices.Count > 0
                ? $"Found {Devices.Count} speaker(s)"
                : "No speakers found — check your network";
        }
        catch (Exception ex)
        {
            SetError($"Discovery failed: {ex.Message}");
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    private bool CanScan() => !IsDiscovering;

    // ── Per-device streaming ───────────────────────────────────────────────────

    public async Task StartDeviceAsync(DeviceViewModel deviceVm)
    {
        deviceVm.IsBusy = true;
        ClearError();
        try
        {
            EnsureAudioCapture();

            StatusText = $"Connecting to {deviceVm.FriendlyName}…";

            // Stop → SetURI → Play (required ordering per Sonos quirks doc)
            await _control.StopAsync(deviceVm.Device);
            await Task.Delay(300);
            await _control.SetStreamUriAsync(deviceVm.Device, _server.StreamUrl);
            await _control.PlayAsync(deviceVm.Device);

            deviceVm.IsStreaming = true;
            StatusText = $"Streaming to {deviceVm.FriendlyName}";
            ActiveConnections = _server.ActiveConnections;

            // Persist so we auto-reconnect next launch
            if (!_settings.Settings.LastUsedDevices.Contains(deviceVm.Device.Id))
            {
                _settings.Settings.LastUsedDevices.Add(deviceVm.Device.Id);
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to start streaming: {ex.Message}");
        }
        finally
        {
            deviceVm.IsBusy = false;
        }
    }

    public async Task StopDeviceAsync(DeviceViewModel deviceVm)
    {
        deviceVm.IsBusy = true;
        try
        {
            await _control.StopAsync(deviceVm.Device);
            deviceVm.IsStreaming = false;
            ActiveConnections = _server.ActiveConnections;

            _settings.Settings.LastUsedDevices.Remove(deviceVm.Device.Id);
            _settings.Save();

            if (!Devices.Any(d => d.IsStreaming))
            {
                _capture.Stop();
                IsCapturing = false;
                StatusText = "Stopped";
            }
            else
            {
                StatusText = $"Streaming to {Devices.Count(d => d.IsStreaming)} speaker(s)";
            }
        }
        catch (Exception ex)
        {
            SetError($"Failed to stop: {ex.Message}");
        }
        finally
        {
            deviceVm.IsBusy = false;
        }
    }

    // ── Global streaming commands ──────────────────────────────────────────────

    [RelayCommand]
    public async Task StreamToAllAsync()
    {
        if (!Devices.Any()) return;

        EnsureAudioCapture();
        ClearError();

        var coordinator = Devices.First();
        // Group all others with the coordinator
        foreach (var device in Devices.Skip(1))
        {
            try { await _control.GroupWithAsync(device.Device, coordinator.Device); }
            catch { /* non-fatal — device may already be standalone */ }
        }

        await StartDeviceAsync(coordinator);
    }

    [RelayCommand]
    public async Task StopAllAsync()
    {
        foreach (var device in Devices.Where(d => d.IsStreaming).ToList())
        {
            try { await StopDeviceAsync(device); }
            catch { /* best effort */ }
        }

        _capture.Stop();
        IsCapturing = false;
        ActiveConnections = 0;
        StatusText = "Stopped";
    }

    // ── Volume ─────────────────────────────────────────────────────────────────

    public async Task SetVolumeAsync(SonosDevice device, int volume)
    {
        try
        {
            await _control.SetVolumeAsync(device, volume);
        }
        catch
        {
            // Non-fatal — volume may fail silently
        }
    }

    // ── Health check / auto-reconnect ──────────────────────────────────────────

    private async Task HealthCheckAsync()
    {
        if (!_settings.Settings.AutoReconnect) return;

        foreach (var dvm in Devices.Where(d => d.IsStreaming).ToList())
        {
            try
            {
                var state = await _control.GetTransportStateAsync(dvm.Device);
                if (state is "STOPPED" or "NO_MEDIA_PRESENT")
                {
                    await _control.SetStreamUriAsync(dvm.Device, _server.StreamUrl);
                    await _control.PlayAsync(dvm.Device);
                }
            }
            catch { /* best effort */ }
        }

        ActiveConnections = _server.ActiveConnections;
    }

    private async Task AutoReconnectLastDevicesAsync()
    {
        var lastIds = _settings.Settings.LastUsedDevices;
        foreach (var dvm in Devices.Where(d => lastIds.Contains(d.Device.Id)))
        {
            await StartDeviceAsync(dvm);
        }
    }

    // ── Discovery collection sync ──────────────────────────────────────────────

    private void OnDiscoveredDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Sync()
        {
            var existingIds = Devices.Select(d => d.Device.Id).ToHashSet();
            var discoveredIds = _discovery.Devices.Select(d => d.Id).ToHashSet();

            // Add newly discovered
            foreach (var dev in _discovery.Devices.Where(d => !existingIds.Contains(d.Id)))
                Devices.Add(new DeviceViewModel(dev, this));

            // Remove stale (not streaming)
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                var dvm = Devices[i];
                if (!discoveredIds.Contains(dvm.Device.Id) && !dvm.IsStreaming)
                    Devices.RemoveAt(i);
            }
        }

        if (_dispatcherQueue != null)
            _dispatcherQueue.TryEnqueue(Sync);
        else
            Sync();
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private void EnsureAudioCapture()
    {
        if (!_capture.IsCapturing)
        {
            _capture.Start();
            IsCapturing = true;
        }
    }

    private void SetError(string msg)
    {
        ErrorMessage = msg;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    public void Cleanup()
    {
        _appCts.Cancel();
        _healthCheckTimer?.Stop();
        _discovery.StopPeriodicScan();
        _capture.Stop();
    }
}
