using System.Diagnostics;
using WqvLink.Core.Imaging;
using WqvLink.Core.Protocol;
using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Transport;

/// <summary>
/// a pretend watch in software
/// It answers exactly like the real one, and can deliberately drop replies, corrupt them or echo, to test the recovery paths.
/// The tests use it, and so does the CLI's --fake-dump option
/// I over-engineered ts
/// </summary>
public sealed record FakeWatchOptions
{
    //Image data bytes per data packet (the real watch sends 128)
    public int PacketSize { get; init; } = PacketPayload;

    //Delay before each reply becomes readable
    public TimeSpan Latency { get; init; } = TimeSpan.Zero;

    //Drop every n-th reply (0 = never). Counts all replies.
    public int DropEveryNth { get; init; }

    //Corrupt the checksum of every n-th reply (0 = never)
    public int CorruptEveryNth { get; init; }

    //Echo every frame we receive back before replying, like an optical reflection
    public bool EchoOwnFrames { get; init; }

    //How many times the watch sends its 63 connect reply (the real one repeats it)
    public int UaRepeats { get; init; } = 1;

    //Hellos to ignore before answering, to exercise the retry loop
    public int IgnoreHellos { get; init; }

    //Address the watch picks; null means it takes the one offered
    public byte? WatchAddress { get; init; }

    //Clock bytes returned in the hello reply
    public byte[] Clock { get; init; } = [0x0C, 0x24, 0x3B, 0xA8];

    //Bytes appended after the records (padding the real watch may send)
    public byte[] Extra { get; init; } = [];

    //Refuse the computed close frame so the 0x54 fallback is used
    public bool RejectComputedClose { get; init; }

    //Never answer the close or disconnect frames
    public bool IgnoreDisconnect { get; init; }

    //Stop answering entirely once this many data packets have been sent
    public int? GoSilentAfterPackets { get; init; }
}


/// Simulates a WQV-1 behind the bridge - for tests and demos without hardware.
/// Every reply is idempotent, so the host's retries behave as on the real watch
public sealed class FakeWatchTransport : ITransport
{
    private enum State { Idle, Connected, RequestedAll, HeaderSent, Data, Closing, Disconnected }

    private readonly object _lock = new();
    private readonly FakeWatchOptions _opts;
    private readonly byte[] _payload;
    private readonly int _imageCount;
    private readonly FrameParser _parser = new();
    private readonly List<(long ReadyAt, byte[] Bytes)> _outbox = [];
    private State _state = State.Idle;
    private byte _adr;
    private int _hellosSeen;
    private int _replies;
    private int _packet;          // index of the packet the watch will send next
    private bool _packetSent;     // whether _packet has been sent at least once
    private long _lastReadyAt;

    public FakeWatchTransport(IReadOnlyList<WqvImage> images, FakeWatchOptions? opts = null)
        : this(images.SelectMany(EncodeRecord).ToArray(), images.Count, opts)
    {
    }

    private FakeWatchTransport(byte[] records, int count, FakeWatchOptions? opts)
    {
        _opts = opts ?? new FakeWatchOptions();
        _imageCount = count;
        _payload = [.. records.AsSpan(0, count * RecordSize), .. _opts.Extra];
    }

    //Replays raw records byte for byte (for example a real dump.bin). Trailing partial records are dropped.
    public static FakeWatchTransport FromRecords(byte[] records, FakeWatchOptions? opts = null) =>
        new(records, records.Length / RecordSize, opts);

    //Every frame the host sent, in order.  
    public List<Frame> HostFrames { get; } = [];

    //Data packets actually transmitted
    public int PacketsTransmitted { get; private set; }

    public bool Disconnected => _state == State.Disconnected;

    //The raw image stream the fake will deliver
    public ReadOnlyMemory<byte> Payload => _payload;

    public Task OpenAsync(CancellationToken ct) => Task.CompletedTask;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            foreach (var f in _parser.Feed(data.Span))
            {
                HostFrames.Add(f);
                if (_opts.EchoOwnFrames)
                {
                    Enqueue(Framing.Build(f.Addr, f.Ctrl, f.Data.Span), TimeSpan.Zero);
                }
                Handle(f);
            }
        }
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long now = Stopwatch.GetTimestamp(), wakeAt = deadline;
            lock (_lock)
            {
                if (_outbox.Count > 0)
                {
                    var (readyAt, bytes) = _outbox[0];
                    if (readyAt <= now)
                    {
                        var n = Math.Min(buffer.Length, bytes.Length);
                        bytes.AsSpan(0, n).CopyTo(buffer.Span);
                        if (n == bytes.Length)
                        {
                            _outbox.RemoveAt(0);
                        }
                        else
                        {
                            _outbox[0] = (readyAt, bytes[n..]);
                        }
                        return n;
                    }
                    wakeAt = Math.Min(deadline, readyAt);
                }
            }
            if (now >= deadline)
            {
                return 0;
            }
            var wait = TimeSpan.FromSeconds(Math.Max(0, wakeAt - now) / (double)Stopwatch.Frequency);
            await Task.Delay(wait < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : wait, ct).ConfigureAwait(false);
        }
    }

    public void DiscardInput()
    {
        lock (_lock)
        {
            var now = Stopwatch.GetTimestamp();
            _outbox.RemoveAll(o => o.ReadyAt <= now);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    //Queues raw bytes as if they arrived over IR (for sniffer tests).
    public void InjectRaw(byte[] bytes)
    {
        lock (_lock)
        {
            Enqueue(bytes, TimeSpan.Zero);
        }
    }

    private void Handle(Frame f)
    {
        var d = f.Data.Span;
        if (f.Addr == BroadcastAddr && f.Ctrl == CtrlHello)
        {
            if (++_hellosSeen > _opts.IgnoreHellos && _state is State.Idle or State.Disconnected)
            {
                Reply(BroadcastAddr, CtrlHelloReply, _opts.Clock);
            }
            return;
        }
        if (f.Addr == BroadcastAddr && f.Ctrl == CtrlConnect && d.Length >= ClockBytes + 1)
        {
            _adr = _opts.WatchAddress ?? d[ClockBytes];
            _state = State.Connected;
            for (var i = 0; i < _opts.UaRepeats; i++)
            {
                //the real watch repeats 63 about every 20 ms (see samples/wqvlink.log)
                Reply(_adr, CtrlUa, [], TimeSpan.FromMilliseconds(20 * i));
            }
            return;
        }
        if (f.Addr != _adr || _state == State.Idle || (_state == State.Disconnected && f.Ctrl != CtrlDisconnect))
        {
            return;
        }

        if (_opts.IgnoreDisconnect && (f.Ctrl == CtrlDisconnect || (f.Ctrl & 0x1F) == 0x14))
        {
            return;
        }

        switch (f.Ctrl)
        {
            case CtrlReady when _state == State.Connected:
                Reply(_adr, CtrlReadyReply, []);
                return;
            case CtrlRequestAll when d.SequenceEqual([RequestAllData]):
                _state = State.RequestedAll;
                Reply(_adr, CtrlRequestAllReply, []);
                return;
            case CtrlReady when _state is State.RequestedAll or State.HeaderSent:
                _state = State.HeaderSent;
                Reply(_adr, CtrlHeaderReply, [0x07, 0xFA, RecordSize >> 8, RecordSize & 0xFF, (byte)_imageCount]);
                return;
            case CtrlStartData when _state is State.HeaderSent or State.Data && d.SequenceEqual([StartDataData]):
                _state = State.Data;
                _packet = 0;
                _packetSent = false;
                Reply(_adr, CtrlStartDataReply, []);
                return;
            case CtrlDisconnect:
                _state = State.Disconnected;
                Reply(_adr, CtrlUa, []);
                return;
        }

        if ((f.Ctrl & 0x1F) == 0x11 && _state == State.Data)
        {
            HandleDataPoll(f.Ctrl >> 5);
        }
        else if ((f.Ctrl & 0x1F) == 0x14 && _state is State.Data or State.Closing)
        {
            // Close I-frame. The watch acks with RR, Nr = the close frame's Ns + 1: 0x61 for Ns = 2
            if (_opts.RejectComputedClose && f.Ctrl != CtrlCloseFallback)
            {
                return;
            }
            _state = State.Closing;
            var ns = (f.Ctrl >> 1) & 7;
            Reply(_adr, (byte)((((ns + 1) & 7) << 5) | 0x01), []);
        }
    }

    //RR with Nr = n. If n acknowledges the packet already sent, advance.
    //either way send the packet with Ns = n. A repeated Nr (host timed out) retransmits the same packet
    private void HandleDataPoll(int nr)
    {
        if (_packetSent && nr == ((_packet + 2) & 7))
        {
            _packet++;
            _packetSent = false;
        }
        if (nr != ((_packet + 1) & 7))
        {
            return;
        }
        if (_opts.GoSilentAfterPackets is { } silent && _packet >= silent)
        {
            return;
        }

        var start = _packet * _opts.PacketSize;
        var chunk = start < _payload.Length ? _payload.AsSpan(start, Math.Min(_opts.PacketSize, _payload.Length - start)) : [];
        var data = new byte[chunk.Length + 1];
        data[0] = DataPacketPrefix;
        chunk.CopyTo(data.AsSpan(1));
        _packetSent = true;
        PacketsTransmitted++;
        Reply(_adr, Sequence.Ret(_packet), data);
    }

    private void Reply(byte addr, byte ctrl, ReadOnlySpan<byte> data, TimeSpan extraDelay = default)
    {
        _replies++;
        if (_opts.DropEveryNth > 0 && _replies % _opts.DropEveryNth == 0)
        {
            return;
        }
        var frame = Framing.Build(addr, ctrl, data);
        if (_opts.CorruptEveryNth > 0 && _replies % _opts.CorruptEveryNth == 0)
        {
            frame[^2] ^= 0x01; // last byte before EOF - the checksum (or its escape), so the parser must discard it
        }
        Enqueue(frame, _opts.Latency + extraDelay);
    }

    private void Enqueue(byte[] bytes, TimeSpan delay)
    {
        // One serial line, nothing can arrive before what was queued earlier.
        var readyAt = Math.Max(Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency), _lastReadyAt);
        _lastReadyAt = readyAt;
        _outbox.Add((readyAt, bytes));
    }

    //Re-encodes a decoded image into its 7229-byte record
    public static byte[] EncodeRecord(WqvImage img)
    {
        var rec = new byte[RecordSize];
        var name = System.Text.Encoding.ASCII.GetBytes(img.Name);
        Array.Fill(rec, (byte)' ', 0, NameLength);
        name.AsSpan(0, Math.Min(name.Length, NameLength)).CopyTo(rec);
        img.DateBytes.AsSpan(0, Math.Min(DateLength, img.DateBytes.Length)).CopyTo(rec.AsSpan(NameLength));
        img.Pixels4bpp.AsSpan(0, Math.Min(PixelBytes, img.Pixels4bpp.Length)).CopyTo(rec.AsSpan(NameLength + DateLength));
        return rec;
    }
}
