using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SonosStream.ViewModels;

namespace SonosStream.Views;

/// <summary>
/// Code-behind for the main speaker-list page.
/// Binds <see cref="MainViewModel"/> state to XAML controls and handles
/// events that are awkward to express as pure MVVM bindings (e.g., debounced slider).
/// </summary>
public sealed partial class DeviceListPage : Page
{
    private MainViewModel? _vm;
    private DispatcherTimer? _volumeDebounceTimer;
    private DeviceViewModel? _pendingVolumeDevice;
    private int _pendingVolume;

    public DeviceListPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (_vm == null) return;

        // Wire up ViewModel → UI (non-bindable controls)
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Populate device list
        DeviceItemsControl.ItemsSource = _vm.Devices;

        // Initial UI state
        RefreshStatus();
        RefreshErrorBar();
    }

    // ── ViewModel property change handlers ─────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.StatusText):
                case nameof(MainViewModel.IsCapturing):
                case nameof(MainViewModel.ActiveConnections):
                case nameof(MainViewModel.StreamUrl):
                    RefreshStatus();
                    break;

                case nameof(MainViewModel.IsDiscovering):
                    ScanProgressRing.IsActive = _vm!.IsDiscovering;
                    RescanButton.IsEnabled = !_vm.IsDiscovering;
                    break;

                case nameof(MainViewModel.HasError):
                case nameof(MainViewModel.ErrorMessage):
                    RefreshErrorBar();
                    break;
            }
        });
    }

    private void RefreshStatus()
    {
        if (_vm == null) return;
        StatusTextBlock.Text = _vm.StatusText;

        if (_vm.IsCapturing && _vm.ActiveConnections > 0)
            StreamUrlBlock.Text = $"Stream: {_vm.StreamUrl}  •  {_vm.ActiveConnections} connection(s)";
        else if (_vm.StreamUrl.Length > 0)
            StreamUrlBlock.Text = $"URL: {_vm.StreamUrl}";
        else
            StreamUrlBlock.Text = string.Empty;

        EmptyStatePanel.Visibility = _vm.Devices.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshErrorBar()
    {
        if (_vm == null) return;
        ErrorInfoBar.IsOpen = _vm.HasError;
        ErrorInfoBar.Message = _vm.ErrorMessage;
    }

    // ── Button event handlers ──────────────────────────────────────────────────

    private async void StreamAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        await _vm.StreamToAllAsync();
    }

    private async void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        await _vm.StopAllAsync();
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        await _vm.ScanAsync();
        RefreshStatus();
    }

    // ── Volume slider (debounced) ──────────────────────────────────────────────

    private void VolumeSlider_ValueChanged(object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.Tag is DeviceViewModel device)
        {
            _pendingVolumeDevice = device;
            _pendingVolume = (int)e.NewValue;

            _volumeDebounceTimer?.Stop();
            _volumeDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _volumeDebounceTimer.Tick += async (_, _) =>
            {
                _volumeDebounceTimer?.Stop();
                if (_pendingVolumeDevice != null)
                    await _pendingVolumeDevice.SetVolumeAsync(_pendingVolume);
            };
            _volumeDebounceTimer.Start();
        }
    }
}
