using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NAudio.CoreAudioApi;
using SonosStream.Models;
using SonosStream.Services;

namespace SonosStream.Views;

/// <summary>
/// Settings page code-behind. Reads from and writes to <see cref="AppSettings"/>
/// via <see cref="SettingsService"/>.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private SettingsService? _settingsService;
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settingsService = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
        if (_settingsService == null) return;

        var s = _settingsService.Settings;

        // Populate audio device dropdown
        PopulateAudioDevices(s.AudioDeviceId);

        // Apply persisted values
        BufferSizeBox.Value = s.BufferSizeSeconds;
        SilenceInjectionToggle.IsOn = s.SilenceInjection;
        ServerPortBox.Value = s.ServerPort;
        AutoReconnectToggle.IsOn = s.AutoReconnect;
        ReconnectIntervalBox.Value = s.AutoReconnectIntervalSeconds;

        _loaded = true;
    }

    // ── Audio device enumeration ───────────────────────────────────────────────

    private void PopulateAudioDevices(string? selectedId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            AudioDeviceCombo.Items.Clear();

            // "System default" option
            AudioDeviceCombo.Items.Add(new ComboBoxItem
            {
                Content = "System default",
                Tag = (string?)null
            });

            int selectedIndex = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                AudioDeviceCombo.Items.Add(new ComboBoxItem
                {
                    Content = dev.FriendlyName,
                    Tag = dev.ID
                });
                if (dev.ID == selectedId) selectedIndex = i + 1;
            }

            AudioDeviceCombo.SelectedIndex = selectedIndex;
        }
        catch
        {
            AudioDeviceCombo.Items.Add(new ComboBoxItem
            {
                Content = "Unable to enumerate devices",
                IsEnabled = false
            });
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void AudioDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* Saved on button press */ }
    private void BufferSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { }
    private void SilenceInjectionToggle_Toggled(object sender, RoutedEventArgs e) { }
    private void ServerPortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { }
    private void AutoReconnectToggle_Toggled(object sender, RoutedEventArgs e) { }
    private void ReconnectIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) { }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _settingsService == null) return;

        var s = _settingsService.Settings;

        // Audio device
        if (AudioDeviceCombo.SelectedItem is ComboBoxItem deviceItem)
            s.AudioDeviceId = deviceItem.Tag as string;

        // Buffer size
        if (!double.IsNaN(BufferSizeBox.Value))
            s.BufferSizeSeconds = (int)BufferSizeBox.Value;

        // Silence injection
        s.SilenceInjection = SilenceInjectionToggle.IsOn;

        // Server port
        if (!double.IsNaN(ServerPortBox.Value))
            s.ServerPort = (int)ServerPortBox.Value;

        // Auto-reconnect
        s.AutoReconnect = AutoReconnectToggle.IsOn;
        if (!double.IsNaN(ReconnectIntervalBox.Value))
            s.AutoReconnectIntervalSeconds = (int)ReconnectIntervalBox.Value;

        _settingsService.Save();

        // Visual feedback
        SaveButton.Content = "Saved!";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SaveButton.Content = "Save Settings";
        };
        timer.Start();
    }
}
