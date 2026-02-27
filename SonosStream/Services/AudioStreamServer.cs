using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SonosStream.Helpers;

namespace SonosStream.Services;

/// <summary>
/// Embedded Kestrel HTTP server that serves the audio stream on
/// GET /stream/swyh.wav
///
/// The URL MUST end in .wav — Sonos determines the audio format from the
/// URL file extension and ignores Content-Type headers.
///
/// Each connecting Sonos speaker receives:
///   1. A 44-byte WAV header (RIFF size = 0x7FFFFFFF = infinite stream)
///   2. Continuous PCM audio data read from the shared CircularAudioBuffer
/// </summary>
public sealed class AudioStreamServer : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly CircularAudioBuffer _audioBuffer;
    private int _activeConnections;
    private string _localIp;
    private bool _started;

    public int Port { get; }
    public string StreamUrl => $"http://{_localIp}:{Port}/stream/swyh.wav";
    public int ActiveConnections => _activeConnections;

    public AudioStreamServer(CircularAudioBuffer audioBuffer, SettingsService settingsService)
    {
        _audioBuffer = audioBuffer;
        Port = settingsService.Settings.ServerPort;
        _localIp = NetworkHelper.GetLocalIpAddress();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return;

        var builder = WebApplication.CreateBuilder();

        // Suppress all Kestrel console noise
        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.ListenAnyIP(Port);
        });

        _app = builder.Build();

        // Refresh local IP in case it changed since construction
        _localIp = NetworkHelper.GetLocalIpAddress();

        _app.MapGet("/stream/swyh.wav", HandleStreamRequest);

        _started = true;
        await _app.StartAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_app != null)
            await _app.StopAsync(ct);
    }

    // ── Request handler ────────────────────────────────────────────────────────

    private async Task HandleStreamRequest(HttpContext ctx)
    {
        var ct = ctx.RequestAborted;

        Interlocked.Increment(ref _activeConnections);
        try
        {
            // ── Response headers ───────────────────────────────────────────────
            ctx.Response.ContentType = "audio/wav";
            ctx.Response.Headers["Transfer-Encoding"] = "chunked";
            ctx.Response.Headers["Connection"] = "keep-alive";
            ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
            ctx.Response.Headers["Pragma"] = "no-cache";

            // ── WAV header (44 bytes) ─────────────────────────────────────────
            var wavHeader = WavHeaderWriter.CreateWavHeader();
            await ctx.Response.Body.WriteAsync(wavHeader, ct);
            await ctx.Response.Body.FlushAsync(ct);

            // ── Infinite audio stream ─────────────────────────────────────────
            // Start reading from the current write position ("live" stream)
            long readerPosition = _audioBuffer.WritePosition;
            var chunk = new byte[4096]; // ~23 ms of audio per chunk

            while (!ct.IsCancellationRequested)
            {
                int bytesRead = _audioBuffer.Read(chunk, 0, chunk.Length, ref readerPosition);

                if (bytesRead > 0)
                {
                    await ctx.Response.Body.WriteAsync(chunk.AsMemory(0, bytesRead), ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                else
                {
                    // No data yet — brief sleep to avoid busy-spinning
                    await Task.Delay(5, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal disconnection
        }
        catch (Exception)
        {
            // Log silently — connection dropped
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    // ── IAsyncDisposable ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
            await _app.DisposeAsync();
    }
}
