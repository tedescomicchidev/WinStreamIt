using System.Threading;

namespace SonosStream.Helpers;

/// <summary>
/// Lock-free ring buffer shared between one audio-capture writer thread
/// and multiple HTTP-streaming reader threads (one per Sonos connection).
///
/// Write position is a monotonically increasing long (never wraps).
/// Array index = writePosition % capacity.
/// Each reader maintains its own read position (also a monotonically increasing long).
/// </summary>
public sealed class CircularAudioBuffer
{
    private readonly byte[] _buffer;
    private readonly int _capacity;
    private long _writePosition; // Monotonically increasing

    public CircularAudioBuffer(int capacityBytes)
    {
        if (capacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBytes));
        _capacity = capacityBytes;
        _buffer = new byte[capacityBytes];
    }

    /// <summary>Current write position (monotonically increasing byte count written).</summary>
    public long WritePosition => Interlocked.Read(ref _writePosition);

    /// <summary>
    /// Number of bytes available for a reader starting at <paramref name="readerPosition"/>.
    /// Clamped to capacity — if the reader has fallen too far behind it will be snapped forward.
    /// </summary>
    public int Available(long readerPosition)
    {
        long wp = WritePosition;
        long available = wp - readerPosition;
        if (available <= 0) return 0;
        if (available > _capacity) available = _capacity;
        return (int)available;
    }

    /// <summary>
    /// Write <paramref name="count"/> bytes from <paramref name="data"/> into the ring buffer.
    /// Overwrites the oldest data when the buffer is full (expected behaviour).
    /// Called exclusively from the audio-capture thread.
    /// </summary>
    public void Write(byte[] data, int offset, int count)
    {
        if (count <= 0) return;

        long wp = _writePosition; // Only writer touches this
        int arrayIndex = (int)(wp % _capacity);
        int remaining = count;
        int srcOffset = offset;

        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, _capacity - arrayIndex);
            Array.Copy(data, srcOffset, _buffer, arrayIndex, chunk);
            srcOffset += chunk;
            remaining -= chunk;
            arrayIndex = (arrayIndex + chunk) % _capacity;
        }

        // Publish the new write position atomically so readers see it
        Interlocked.Add(ref _writePosition, count);
    }

    /// <summary>
    /// Read up to <paramref name="count"/> bytes into <paramref name="buffer"/>.
    /// Updates <paramref name="readerPosition"/> by the number of bytes actually read.
    /// If the reader has fallen behind by more than capacity it is snapped forward.
    /// Returns the number of bytes read (0 if no data is currently available).
    /// </summary>
    public int Read(byte[] buffer, int offset, int count, ref long readerPosition)
    {
        long wp = Interlocked.Read(ref _writePosition);
        long available = wp - readerPosition;

        if (available <= 0) return 0;

        // Snap reader forward if it has fallen behind
        if (available > _capacity)
        {
            // Leave a small margin (1/4 of capacity) to avoid re-lagging immediately
            readerPosition = wp - (_capacity / 4 * 3);
            available = _capacity / 4 * 3;
        }

        int toRead = (int)Math.Min(count, available);
        int arrayIndex = (int)(readerPosition % _capacity);
        int remaining = toRead;
        int dstOffset = offset;

        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, _capacity - arrayIndex);
            Array.Copy(_buffer, arrayIndex, buffer, dstOffset, chunk);
            dstOffset += chunk;
            remaining -= chunk;
            arrayIndex = (arrayIndex + chunk) % _capacity;
        }

        readerPosition += toRead;
        return toRead;
    }
}
