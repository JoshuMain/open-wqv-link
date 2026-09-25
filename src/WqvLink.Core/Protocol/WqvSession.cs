using System.Diagnostics;
using WqvLink.Core.Logging;
using WqvLink.Core.Transport;
using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Protocol;

/// <summary>
/// Drives the WQV-1 protocol over a transport, Half-duplex and PC-driven 
/// every exchange clears the receive side and sends one frame - then reads until a matching reply or the deadline
/// </summary>
public sealed class WqvSession
{
    private readonly ITransport _transport;
    private readonly ByteTraceLog _log;
    private readonly SessionOptions _opts;
    private readonly FrameParser _parser = new();
    private readonly Queue<Frame> _pending = new();
    private readonly byte[] _readBuffer = new byte[4096];
    private Frame? _lastSent;

    public WqvSession(ITransport transport, ByteTraceLog log, SessionOptions? opts = null)
    {
        _transport = transport;
        _log = log;
        _opts = opts ?? SessionOptions.Default;
        _parser.Discarded += reason =>
        {
            if (reason.StartsWith("checksum", StringComparison.Ordinal))
            {
                ChecksumErrors++;
            }
            _log.Note(reason);
        };
    }

    //The address from the watch's connect reply, once connected
    public byte? Address { get; private set; }

    //Frames discarded for bad checksums so far
    public int ChecksumErrors { get; private set; }

    public Task<WatchInfo> ConnectAsync(CancellationToken ct) => ConnectAsync(null, ct);

    //Handshakeattempts reports each hello sent
    public async Task<WatchInfo> ConnectAsync(IProgress<int>? helloAttempts, CancellationToken ct)
    {
        var hello = await XferAsync(BroadcastAddr, CtrlHello, [],
            f => f.Addr == BroadcastAddr && f.Ctrl == CtrlHelloReply && f.Data.Length >= ClockBytes,
            _opts.HelloTries, _opts.HelloTimeout, "FF B3 hello", SessionStage.Hello, ct, helloAttempts).ConfigureAwait(false);
        var clock = hello.Data[..ClockBytes].ToArray();
        _log.Note($"watch answered, clock {Hex.Format(clock)}");

        var connect = await XferAsync(BroadcastAddr, CtrlConnect, [.. clock, _opts.AssignedAddress],
            f => f.Ctrl == CtrlUa, _opts.ControlTries, _opts.ControlTimeout, "FF 93 connect", SessionStage.Connect, ct).ConfigureAwait(false);
        var adr = connect.Addr;
        Address = adr;
        _log.Note($"connected, watch uses address {adr:X2}");

        // The watch may repeat its 63 reply, the xfer loop reads past those until it sees 01
        await XferAsync(adr, CtrlReady, [],
            f => f.Addr == adr && f.Ctrl == CtrlReadyReply, _opts.ControlTries, _opts.ControlTimeout, "11 ready", SessionStage.Connect, ct).ConfigureAwait(false);
        return new WatchInfo(clock, adr);
    }

    //Downloads every image, then closes the transfer and disconnects.
    //On cancellation a best-effort disconnect is attempted with a short timeout before rethrowing
    public async Task<DownloadResult> DownloadAllAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var adr = Address ?? throw new InvalidOperationException("Call ConnectAsync first");
        var packets = 0;
        try
        {
            await XferAsync(adr, CtrlRequestAll, [RequestAllData], f => f.Ctrl == CtrlRequestAllReply,
                _opts.ControlTries, _opts.ControlTimeout, "10 01 (all images)", SessionStage.Header, ct).ConfigureAwait(false);
            var header = await XferAsync(adr, CtrlReady, [], f => f.Ctrl == CtrlHeaderReply && f.Data.Length >= HeaderLength,
                _opts.ControlTries, _opts.ControlTimeout, "11 poll", SessionStage.Header, ct).ConfigureAwait(false);

            var h = header.Data.ToArray();
            var size = (h[2] << 8) | h[3];
            int count = h[4];
            _log.Note($"watch header: {Hex.Format(h)} -> {count} image(s) x {size} bytes");
            if (size != RecordSize)
            {
                _log.Note($"WARNING: record size {size} != expected {RecordSize}; decoding may be off");
            }
            if (_opts.ExpectedCount is { } expect)
            {
                _log.Note($"overriding image count -> {expect}");
                count = expect;
            }

            await XferAsync(adr, CtrlStartData, [StartDataData], f => f.Ctrl == CtrlStartDataReply,
                _opts.ControlTries, _opts.ControlTimeout, "32 06", SessionStage.Header, ct).ConfigureAwait(false);

            long total = (long)count * size;
            var buffer = new List<byte>((int)Math.Min(total + PacketPayload, int.MaxValue));
            var clock = Stopwatch.StartNew();
            var warned = false;
            progress?.Report(new DownloadProgress(0, total, 0, 0, null, count));

            while (buffer.Count < total)
            {
                var get = Sequence.Get(packets);
                var ret = Sequence.Ret(packets);
                var frame = await XferAsync(adr, get, [], f => f.Ctrl == ret,
                    _opts.DataTries, _opts.DataTimeout, $"get {get:X2}", SessionStage.Data, ct).ConfigureAwait(false);

                // Each i is appended exactly once, retries resend the same get and only the first matching reply is accepted
                var data = frame.Data;
                if (data.Length > 0 && data.Span[0] == DataPacketPrefix)
                {
                    data = data[1..];
                }
                else if (!warned)
                {
                    _log.Note($"note: data packet didn't start with 05 ({Hex.Format(data.Span[..Math.Min(4, data.Length)])})");
                    warned = true;
                }
                buffer.AddRange(data.ToArray());
                packets++;

                var elapsed = clock.Elapsed.TotalSeconds;
                var rate = buffer.Count / Math.Max(0.001, elapsed);
                TimeSpan? eta = rate > 0 ? TimeSpan.FromSeconds(Math.Max(0, total - buffer.Count) / rate) : null;
                progress?.Report(new DownloadProgress(Math.Min(buffer.Count, total), total, packets, rate, eta, count));
            }

            try
            {
                await CloseAsync(adr, Sequence.CloseNr(packets), _opts.DisconnectTries, _opts.ControlTimeout, ct).ConfigureAwait(false);
            }
            catch (WqvLinkException ex)
            {
                // All the data is in, a lost close/disconnect reply must not throw it away
                // The watch times out of COM mode on its own
                _log.Note($"WARNING: {ex.Message} Keeping the downloaded data.");
                Address = null;
            }

            var all = buffer.ToArray();
            return new DownloadResult(all[..(int)total], all[(int)total..], count, size);
        }
        catch (OperationCanceledException)
        {
            await BestEffortCloseAsync(adr, Sequence.CloseNr(packets)).ConfigureAwait(false);
            throw;
        }
    }

    //Plain disconnect, as used after a ping
    public async Task DisconnectAsync(CancellationToken ct)
    {
        var adr = Address ?? throw new InvalidOperationException("Not connected");
        await XferAsync(adr, CtrlDisconnect, [], f => f.Ctrl == CtrlUa,
            _opts.DisconnectTries, _opts.ControlTimeout, "53 disconnect", SessionStage.Disconnect, ct).ConfigureAwait(false);
        Address = null;
        _log.Note("disconnected cleanly");
    }

    //Close after data, the I-frame, falling back to the literal 0x54
    private async Task CloseAsync(byte adr, int nr, int tries, TimeSpan timeout, CancellationToken ct)
    {
        static bool Ack(Frame f) => (f.Ctrl & 0x0F) == 0x01;
        try
        {
            await XferAsync(adr, Sequence.CloseCtrl(nr), [StartDataData], Ack, tries, timeout, "end-of-transfer", SessionStage.Disconnect, ct).ConfigureAwait(false);
        }
        catch (WqvLinkException)
        {
            await XferAsync(adr, CtrlCloseFallback, [StartDataData], Ack, tries, timeout, "54 06", SessionStage.Disconnect, ct).ConfigureAwait(false);
        }
        await XferAsync(adr, CtrlDisconnect, [], f => f.Ctrl == CtrlUa, tries, timeout, "53 disconnect", SessionStage.Disconnect, ct).ConfigureAwait(false);
        Address = null;
        _log.Note("disconnected cleanly");
    }

    private async Task BestEffortCloseAsync(byte adr, int nr)
    {
        _log.Note("cancelled; attempting a clean disconnect");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await CloseAsync(adr, nr, 1, _opts.CancelDisconnectTimeout, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WqvLinkException or OperationCanceledException or TransportException)
        {
            _log.Note($"disconnect after cancel failed: {ex.Message}");
        }
    }

    //Sends a frame and waits for a reply matching resends on timeout
    private async Task<Frame> XferAsync(byte addr, byte ctrl, byte[] data, Func<Frame, bool> want, int tries, TimeSpan timeout,
        string what, SessionStage stage, CancellationToken ct, IProgress<int>? attempts = null)
    {
        for (var attempt = 1; attempt <= tries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            attempts?.Report(attempt);
            await SendAsync(addr, ctrl, data, ct).ConfigureAwait(false);
            var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
            while (true)
            {
                var f = await ReceiveAsync(deadline, ct).ConfigureAwait(false);
                if (f is null)
                {
                    break;
                }
                if (want(f))
                {
                    return f;
                }
                _log.Note($"unexpected {f}");
            }
        }
        throw new WqvLinkException(stage, $"No valid reply to {what} after {tries} tries.", ChecksumErrors);
    }

    private async Task SendAsync(byte addr, byte ctrl, byte[] data, CancellationToken ct)
    {
        _parser.Reset();
        _pending.Clear();
        _transport.DiscardInput();
        var raw = Framing.Build(addr, ctrl, data);
        _lastSent = new Frame(addr, ctrl, data);
        _log.Sent(raw);
        await _transport.WriteAsync(raw, ct).ConfigureAwait(false);
    }

    private void Absorb(int n)
    {
        var chunk = _readBuffer.AsSpan(0, n);
        _log.Received(chunk);
        foreach (var frame in _parser.Feed(chunk))
        {
            _pending.Enqueue(frame);
        }
    }

    //Next frame that isn't our own optical echo, or null at the deadline
    private async Task<Frame?> ReceiveAsync(long deadline, CancellationToken ct)
    {
        while (true)
        {
            while (_pending.TryDequeue(out var f))
            {
                if (f.Equals(_lastSent))
                {
                    _log.Note("ignored our own optical echo");
                    continue;
                }
                return f;
            }

            var left = TimeSpan.FromSeconds((deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
            if (left <= TimeSpan.Zero)
            {
                return null;
            }
            var n = await _transport.ReadAsync(_readBuffer, left, ct).ConfigureAwait(false);
            if (n > 0)
            {
                Absorb(n);
            }
        }
    }
}
