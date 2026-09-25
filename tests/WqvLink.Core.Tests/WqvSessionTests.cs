using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Tests.TestData;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Tests;

public sealed class WqvSessionTests
{
    /// <summary>Short timeouts so retry paths run quickly; tries stay as specified.</summary>
    private static readonly SessionOptions Fast = new()
    {
        HelloTimeout = TimeSpan.FromMilliseconds(20),
        ControlTimeout = TimeSpan.FromMilliseconds(40),
        DataTimeout = TimeSpan.FromMilliseconds(40),
        CancelDisconnectTimeout = TimeSpan.FromMilliseconds(40),
    };

    private static async Task<(DownloadResult Result, FakeWatchTransport Watch, List<string> Log)> Download(
        int images, FakeWatchOptions? opts = null, SessionOptions? session = null)
    {
        var watch = new FakeWatchTransport(FixtureFactory.Images(images), opts);
        var lines = new List<string>();
        var log = new ByteTraceLog();
        log.Line += lines.Add;
        var s = new WqvSession(watch, log, session ?? Fast);
        await s.ConnectAsync(CancellationToken.None);
        var result = await s.DownloadAllAsync(null, CancellationToken.None);
        return (result, watch, lines);
    }

    [Fact]
    public async Task Connect_ReturnsClockAndAddress()
    {
        var watch = new FakeWatchTransport([], new FakeWatchOptions { UaRepeats = 3 });
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast);
        var info = await s.ConnectAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 0x0C, 0x24, 0x3B, 0xA8 }, info.Clock);
        Assert.Equal(0x02, info.Address);
        Assert.Equal("12:36:59", info.ClockText);
        Assert.Equal(new byte[] { 0x0C, 0x24, 0x3B, 0xA8, 0x02 }, watch.HostFrames[1].Data.ToArray());
    }

    [Fact]
    public async Task Connect_UsesAddressFromWatchReply()
    {
        var watch = new FakeWatchTransport([], new FakeWatchOptions { WatchAddress = 0x05 });
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast);
        var info = await s.ConnectAsync(CancellationToken.None);
        Assert.Equal(0x05, info.Address);
        Assert.Equal(0x05, watch.HostFrames[^1].Addr);
    }

    [Fact]
    public async Task Connect_RetriesHelloUntilAnswered()
    {
        var watch = new FakeWatchTransport([], new FakeWatchOptions { IgnoreHellos = 4 });
        var attempts = new List<int>();
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast);
        await s.ConnectAsync(new SyncProgress<int>(attempts.Add), CancellationToken.None);
        Assert.Equal([1, 2, 3, 4, 5], attempts);
    }

    [Fact]
    public async Task Connect_NoWatchFailsAtHelloStage()
    {
        var watch = new FakeWatchTransport([], new FakeWatchOptions { IgnoreHellos = int.MaxValue });
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast with { HelloTries = 3 });
        var ex = await Assert.ThrowsAsync<WqvLinkException>(() => s.ConnectAsync(CancellationToken.None));
        Assert.Equal(SessionStage.Hello, ex.Stage);
    }

    [Fact]
    public async Task Download_ThreeImagesByteExact()
    {
        var (result, watch, _) = await Download(3);
        Assert.Equal(3, result.ImageCount);
        Assert.Equal(7229, result.RecordSize);
        Assert.Equal(FixtureFactory.Dump(3), result.Raw);
        Assert.True(watch.Disconnected);
    }

    [Fact]
    public async Task Download_ExtraBytesAfterRecordsAreReturnedSeparately()
    {
        var (result, _, _) = await Download(1, new FakeWatchOptions { Extra = [1, 2, 3] });
        Assert.Equal(FixtureFactory.Dump(1), result.Raw);
        // 7229 bytes = 56 full packets + 61 bytes; the extra rides in the last packet.
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Extra);
    }

    [Fact]
    public async Task Download_DroppedRepliesRecoveredWithoutDuplicates()
    {
        var (result, watch, _) = await Download(2, new FakeWatchOptions { DropEveryNth = 7 });
        Assert.Equal(FixtureFactory.Dump(2), result.Raw);
        Assert.True(watch.PacketsTransmitted > (2 * 7229 + 127) / 128, "some packets must have been retransmitted");
    }

    [Fact]
    public async Task Download_CorruptChecksumsRecovered()
    {
        var (result, _, log) = await Download(1, new FakeWatchOptions { CorruptEveryNth = 5 });
        Assert.Equal(FixtureFactory.Dump(1), result.Raw);
        Assert.Contains(log, l => l.Contains("checksum mismatch"));
    }

    [Fact]
    public async Task Download_EchoedFramesAreIgnored()
    {
        var (result, _, log) = await Download(1, new FakeWatchOptions { EchoOwnFrames = true });
        Assert.Equal(FixtureFactory.Dump(1), result.Raw);
        Assert.Contains(log, l => l.Contains("ignored our own optical echo"));
    }

    [Fact]
    public async Task Download_RepeatedUaIsSkipped()
    {
        var (result, _, log) = await Download(1, new FakeWatchOptions { UaRepeats = 3 });
        Assert.Equal(FixtureFactory.Dump(1), result.Raw);
        Assert.Contains(log, l => l.Contains("unexpected [02 63]"));
    }

    [Fact]
    public async Task Download_ZeroImages()
    {
        var (result, watch, _) = await Download(0);
        Assert.Equal(0, result.ImageCount);
        Assert.Empty(result.Raw);
        Assert.True(watch.Disconnected);
        // No data packets: close uses Nr = 1.
        Assert.Contains(watch.HostFrames, f => f.Ctrl == Sequence.CloseCtrl(1));
    }

    [Fact]
    public async Task Download_CloseUsesComputedNr()
    {
        // 1 image = 57 packets -> Nr = 58 & 7 = 2 -> 0x54. 2 images = 113 packets -> Nr = 114 & 7 = 2.
        // 3 images = 170 packets -> Nr = 171 & 7 = 3 -> 0x74.
        var (_, watch, _) = await Download(3);
        var packets = (3 * 7229 + 127) / 128;
        var nr = Sequence.CloseNr(packets);
        Assert.NotEqual(2, nr);
        Assert.Contains(watch.HostFrames, f => f.Ctrl == Sequence.CloseCtrl(nr) && f.Data.Span.SequenceEqual(new byte[] { 0x06 }));
        Assert.DoesNotContain(watch.HostFrames, f => f.Ctrl == 0x54);
        Assert.Equal(0x53, watch.HostFrames[^1].Ctrl);
    }

    [Fact]
    public async Task Download_CloseFallsBackTo54()
    {
        var (_, watch, _) = await Download(3, new FakeWatchOptions { RejectComputedClose = true });
        Assert.Contains(watch.HostFrames, f => f.Ctrl == 0x54);
        Assert.True(watch.Disconnected);
    }

    [Fact]
    public async Task Download_FailedDisconnectKeepsCompleteData()
    {
        var (result, _, log) = await Download(1, new FakeWatchOptions { IgnoreDisconnect = true });
        Assert.Equal(FixtureFactory.Dump(1), result.Raw);
        Assert.Contains(log, l => l.Contains("Keeping the downloaded data"));
    }

    [Fact]
    public async Task Download_ExpectedCountOverridesHeader()
    {
        var (result, _, _) = await Download(3, session: Fast with { ExpectedCount = 2 });
        Assert.Equal(2, result.ImageCount);
        Assert.Equal(FixtureFactory.Dump(3)[..(2 * 7229)], result.Raw);
    }

    [Fact]
    public async Task Download_ReportsProgress()
    {
        var watch = new FakeWatchTransport(FixtureFactory.Images(1));
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast);
        await s.ConnectAsync(CancellationToken.None);
        var reports = new List<DownloadProgress>();
        await s.DownloadAllAsync(new SyncProgress<DownloadProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(0, reports[0].BytesReceived);
        Assert.Equal(1, reports[0].ImageCount);
        Assert.Equal(7229, reports[^1].BytesReceived);
        Assert.Equal(7229, reports[^1].BytesTotal);
        Assert.Equal(57, reports[^1].Packets);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public async Task Download_CancelMidTransferEndsCleanlyWithDisconnect()
    {
        var watch = new FakeWatchTransport(FixtureFactory.Images(3), new FakeWatchOptions { Latency = TimeSpan.FromMilliseconds(2) });
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast);
        await s.ConnectAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<DownloadProgress>(p =>
        {
            if (p.Packets == 10)
            {
                cts.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.DownloadAllAsync(progress, cts.Token));
        Assert.True(watch.Disconnected, "a best-effort disconnect should have been sent");
        Assert.Contains(watch.HostFrames, f => f.Ctrl == Sequence.CloseCtrl(Sequence.CloseNr(10)));
    }

    [Fact]
    public async Task Download_WatchGoesSilentFailsAtDataStage()
    {
        var watch = new FakeWatchTransport(FixtureFactory.Images(1), new FakeWatchOptions { GoSilentAfterPackets = 5 });
        var s = new WqvSession(watch, ByteTraceLog.Null, Fast);
        await s.ConnectAsync(CancellationToken.None);
        var ex = await Assert.ThrowsAsync<WqvLinkException>(() => s.DownloadAllAsync(null, CancellationToken.None));
        Assert.Equal(SessionStage.Data, ex.Stage);
        Assert.Contains("get D1", ex.Message); // packet index 5 -> Nr 6 -> D1
    }

    [Fact]
    public async Task Trace_MatchesPythonFormat()
    {
        var watch = new FakeWatchTransport([]);
        var lines = new List<string>();
        var t = 12.345;
        var log = new ByteTraceLog(clock: () => t);
        log.Line += lines.Add;
        await new WqvSession(watch, log, Fast).ConnectAsync(CancellationToken.None);
        Assert.Equal("    12.345 > c0 ff b3 01 b2 c1", lines[0]);
        Assert.Equal("    12.345 < c0 ff a3 0c 24 3b a8 02 b5 c1", lines[1]);
    }
}

/// <summary>IProgress that reports synchronously on the caller's thread (unlike Progress&lt;T&gt;).</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
