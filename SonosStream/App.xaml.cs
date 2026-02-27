using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SonosStream.Helpers;
using SonosStream.Services;
using SonosStream.ViewModels;

namespace SonosStream;

/// <summary>
/// Application entry point. Sets up the DI container and launches the main window.
/// </summary>
public partial class App : Application
{
    public IServiceProvider Services { get; }

    public new static App Current => (App)Application.Current;

    private Window? _window;

    public App()
    {
        Services = ConfigureServices();
        InitializeComponent();
    }

    // ── DI registration ────────────────────────────────────────────────────────

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<SettingsService>();

        // CircularAudioBuffer: capacity = settings.BufferSizeSeconds × 176400 bytes/sec
        services.AddSingleton<CircularAudioBuffer>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            int capacity = 176400 * Math.Max(1, settings.Settings.BufferSizeSeconds);
            return new CircularAudioBuffer(capacity);
        });

        // Services
        services.AddSingleton<AudioCaptureService>(sp =>
            new AudioCaptureService(sp.GetRequiredService<CircularAudioBuffer>()));

        services.AddSingleton<AudioStreamServer>();
        services.AddSingleton<SonosDiscoveryService>();
        services.AddSingleton<SonosControlService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }

    // ── Launch ─────────────────────────────────────────────────────────────────

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
