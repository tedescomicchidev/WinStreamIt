using NAudio.Wave;
using SonosStream.Helpers;

namespace SonosStream.Services;

/// <summary>
/// Captures all Windows system audio via WASAPI loopback, resamples it to
/// 16-bit / 44100 Hz / stereo PCM, and writes it continuously into the
/// shared <see cref="CircularAudioBuffer"/>.
///
/// Silence injection ensures the buffer never drains even when no audio is
/// playing, which prevents Sonos from dropping the HTTP connection.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    // ── Output format (Sonos LPCM requirement) ─────────────────────────────────
    public static readonly WaveFormat OutputFormat = new WaveFormat(44100, 16, 2);

    // ── State ──────────────────────────────────────────────────────────────────
    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _bufferedWave;
    private IWaveProvider? _convertingProvider;
    private System.Threading.Timer? _silenceTimer;
    private volatile bool _isCapturing;
    private volatile bool _disposed;
    private DateTime _lastDataTime = DateTime.MinValue;

    // 100 ms worth of silence at 44100/16/stereo = 17640 bytes
    private static readonly byte[] SilenceChunk = new byte[(int)(176400 * 0.1)];

    private readonly CircularAudioBuffer _buffer;

    public bool IsCapturing => _isCapturing;
    public WaveFormat CaptureFormat => _capture?.WaveFormat ?? OutputFormat;
    public CircularAudioBuffer Buffer => _buffer;

    public AudioCaptureService(CircularAudioBuffer buffer)
    {
        _buffer = buffer;
    }

    // ── Start / Stop ───────────────────────────────────────────────────────────

    public void Start()
    {
        if (_isCapturing || _disposed) return;

        _capture = new WasapiLoopbackCapture();
        var captureFormat = _capture.WaveFormat;

        // BufferedWaveProvider holds raw captured samples for the resampler
        _bufferedWave = new BufferedWaveProvider(captureFormat)
        {
            BufferLength = captureFormat.AverageBytesPerSecond * 3, // 3-second raw buffer
            DiscardOnBufferOverflow = true
        };

        // Build conversion pipeline: raw → 16-bit PCM at 44100 Hz
        if (captureFormat.SampleRate == OutputFormat.SampleRate &&
            captureFormat.Channels == OutputFormat.Channels &&
            captureFormat.BitsPerSample == OutputFormat.BitsPerSample)
        {
            // No conversion needed
            _convertingProvider = _bufferedWave;
        }
        else
        {
            // MediaFoundationResampler handles float→int16 and sample-rate conversion
            _convertingProvider = new MediaFoundationResampler(_bufferedWave, OutputFormat)
            {
                ResamplerQuality = 60
            };
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        _isCapturing = true;

        // Silence injection timer — fires every 80 ms
        _silenceTimer = new System.Threading.Timer(InjectSilenceIfNeeded, null, 80, 80);
    }

    public void Stop()
    {
        if (!_isCapturing) return;
        _isCapturing = false;

        _silenceTimer?.Dispose();
        _silenceTimer = null;

        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        _bufferedWave = null;

        if (_convertingProvider is IDisposable d)
            d.Dispose();
        _convertingProvider = null;
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isCapturing || e.BytesRecorded <= 0) return;

        _lastDataTime = DateTime.UtcNow;

        _bufferedWave!.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Drain the converting provider into the circular buffer
        var tmp = new byte[4096];
        int read;
        while (_bufferedWave.BufferedBytes > 0 &&
               (read = _convertingProvider!.Read(tmp, 0, tmp.Length)) > 0)
        {
            _buffer.Write(tmp, 0, read);
        }
    }

    private void InjectSilenceIfNeeded(object? state)
    {
        if (!_isCapturing) return;

        // If no real audio has arrived in the last 150 ms, push silence
        var idle = (DateTime.UtcNow - _lastDataTime).TotalMilliseconds;
        if (idle > 150)
        {
            _buffer.Write(SilenceChunk, 0, SilenceChunk.Length);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Device was removed or an error occurred — mark as stopped
        _isCapturing = false;
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
