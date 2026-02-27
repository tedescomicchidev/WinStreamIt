using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SonosStream.ViewModels;
using SonosStream.Views;

namespace SonosStream;

/// <summary>
/// Shell window that hosts a NavigationView with two pages:
///   • Speakers (DeviceListPage) — default
///   • Settings (SettingsPage) — NavigationView built-in settings item
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "SonosStream";

        // Kick off async initialisation from the UI thread
        ContentFrame.Loaded += async (s, e) =>
        {
            // Navigate to Speakers by default
            NavView.SelectedItem = DevicesNavItem;
            ContentFrame.Navigate(typeof(DeviceListPage));

            // Initialize ViewModel now that we have a DispatcherQueue
            var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
            if (vm != null)
                await vm.InitializeAsync(DispatcherQueue);
        };

        // Clean up services when the window closes
        Closed += (s, e) =>
        {
            var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
            vm?.Cleanup();
        };
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "Devices":
                    ContentFrame.Navigate(typeof(DeviceListPage));
                    break;
            }
        }
    }
}
