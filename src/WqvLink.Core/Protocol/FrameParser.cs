using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Protocol;

/// <summary>
/// Stateful frame parser. Scans for EOF, then takes the bytes
/// after the last BOF before it, so preamble and repeated BOFs are skipped. Bad frames are reported through <see cref="Discarded"/> and never throw.
/// </summary>
public sealed class FrameParser
{
    private readonly List<byte> _buffer = new(512);

    //Raised with a human-readable reason whenever a frame is thrown away
    public event Action<string>? Discarded;

    //Bytes held while waiting for an EOF
    public int Pending => _buffer.Count;

    //Drops any partial frame
    public void Reset() => _buffer.Clear();

    //Appends a chunk and returns every complete, valid frame it finished
    public IReadOnlyList<Frame> Feed(ReadOnlySpan<byte> chunk)
    {
        var frames = new List<Frame>();
        foreach (var b in chunk)
        {
            if (b != Eof)
            {
                _buffer.Add(b);
                continue;
            }

            var bof = _buffer.LastIndexOf(Bof);
            if (bof < 0)
            {
                if (_buffer.Count > 0)
                {
                    Discarded?.Invoke($"EOF without BOF after {_buffer.Count} byte(s)");
                }
                _buffer.Clear();
                continue;
            }

            var segment = _buffer.GetRange(bof + 1, _buffer.Count - bof - 1).ToArray();
            _buffer.Clear();
            var frame = Decode(segment);
            if (frame is not null)
            {
                frames.Add(frame);
            }
        }
        return frames;
    }

    private Frame? Decode(byte[] segment)
    {
        if (!Framing.TryUnescape(segment, out var body))
        {
            Discarded?.Invoke($"dangling escape: {Hex.Format(segment)}");
            return null;
        }
        if (body.Length < MinFrameBody)
        {
            Discarded?.Invoke($"short frame ({body.Length} B): {Hex.Format(body)}");
            return null;
        }
        var expected = (ushort)((body[^2] << 8) | body[^1]);
        if (Checksum.Compute(body.AsSpan(0, body.Length - 2)) != expected)
        {
            Discarded?.Invoke($"checksum mismatch: {Hex.Format(body)}");
            return null;
        }
        return new Frame(body[0], body[1], body.AsSpan(2, body.Length - 4).ToArray());
    }
}
