using System.Text;

namespace SonosStream.Helpers;

/// <summary>
/// Generates a 44-byte WAV/RIFF header suitable for infinite HTTP streaming.
/// The RIFF and data chunk sizes are set to 0x7FFFFFFF to signal an unbounded stream,
/// which Sonos handles correctly.
/// </summary>
public static class WavHeaderWriter
{
    // Output format constants (must match AudioCaptureService output)
    private const int SampleRate = 44100;
    private const short BitsPerSample = 16;
    private const short Channels = 2;
    private const int BlockAlign = Channels * (BitsPerSample / 8); // 4
    private const int ByteRate = SampleRate * BlockAlign;           // 176400
    private const short AudioFormat = 1;                            // PCM

    // 0x7FFFFFFF signals "unknown / infinite" length
    private const int InfiniteSize = int.MaxValue;

    /// <summary>
    /// Returns a 44-byte WAV header for an infinite LPCM stream.
    /// </summary>
    public static byte[] CreateWavHeader()
    {
        using var ms = new MemoryStream(44);
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // RIFF chunk descriptor
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(InfiniteSize);                     // chunk size (infinite)
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt sub-chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                               // sub-chunk size (PCM = 16)
        writer.Write(AudioFormat);                      // 1 = PCM
        writer.Write(Channels);                         // 2
        writer.Write(SampleRate);                       // 44100
        writer.Write(ByteRate);                         // 176400
        writer.Write((short)BlockAlign);                // 4
        writer.Write(BitsPerSample);                    // 16

        // data sub-chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(InfiniteSize);                     // data size (infinite)

        return ms.ToArray();
    }
}
