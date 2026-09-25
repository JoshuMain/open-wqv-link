using WqvLink.Core.Diagnostics;
using WqvLink.Core.Imaging;
using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Storage;
using WqvLink.Core.Tests.TestData;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Tests;

public sealed class StorageAndDiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wqvtest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void SessionStore_CreatesUniqueTimestampedFolders()
    {
        var store = new SessionStore(_dir);
        var now = new DateTime(2026, 9, 25, 13, 0, 9);
        var a = store.CreateSessionFolder(now);
        var b = store.CreateSessionFolder(now);
        Assert.Equal("20260925-130009", Path.GetFileName(a));
        Assert.Equal("20260925-130009-2", Path.GetFileName(b));
    }

    [Fact]
    public void SessionStore_SaveDownloadWritesDumpAndNamedPngs()
    {
        var raw = FixtureFactory.Dump(2);
        var result = new DownloadResult(raw, [9, 9], 2, 7229);
        var paths = SessionStore.SaveDownload(_dir, result, new ExportOptions { Sidecars = true });

        Assert.Equal([.. raw, 9, 9], File.ReadAllBytes(Path.Combine(_dir, "dump.bin")));
        Assert.Equal(["001_20240101-0000_IMG1.png", "002_20240202-0107_IMG2.png"], paths.Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(_dir, "001_20240101-0000_IMG1.json")));
    }

    [Fact]
    public void Settings_RoundTripAndCorruptFileFallsBack()
    {
        var path = Path.Combine(_dir, "settings.json");
        var s = new Settings { ExportScale = 4, SwapNibbles = true, LastPort = "COM10", Theme = ThemeChoice.Dark };
        s.Protocol.DataTries = 12;
        s.Save(path);

        var loaded = Settings.Load(path);
        Assert.Equal(4, loaded.ExportScale);
        Assert.True(loaded.SwapNibbles);
        Assert.Equal("COM10", loaded.LastPort);
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.Equal(12, loaded.Protocol.ToSessionOptions().DataTries);

        File.WriteAllText(path, "{ not json");
        Assert.Equal(1, Settings.Load(path).ExportScale);
    }

    [Fact]
    public async Task Ping_PassesAgainstFakeWatch()
    {
        var watch = new FakeWatchTransport([]);
        var result = await PingTest.RunAsync(watch, ByteTraceLog.Null, null, CancellationToken.None);
        Assert.Equal(DiagVerdict.Pass, result.Verdict);
        Assert.Contains("12:36:59", result.Details);
        Assert.True(watch.Disconnected);
    }

    [Fact]
    public async Task Loopback_PassesOnEchoingTransport()
    {
        var result = await LoopbackTest.RunAsync(new LoopbackTransport(), CancellationToken.None);
        Assert.Equal(DiagVerdict.Pass, result.Verdict);
    }

    [Fact]
    public async Task Echo_PassesOnEchoingTransport()
    {
        var result = await EchoTest.RunAsync(new LoopbackTransport(), CancellationToken.None);
        Assert.Equal(DiagVerdict.Pass, result.Verdict);
        Assert.StartsWith("20/20", result.Message);
    }

    [Fact]
    public async Task Sniffer_PrintsRawAndFrames()
    {
        var watch = new FakeWatchTransport([]);
        watch.InjectRaw([0x12, .. Framing.Build(0xFF, 0xB3)]);
        var lines = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var result = await Sniffer.RunAsync(watch, lines.Add, cts.Token);
        Assert.Equal("raw: 12 c0 ff b3 01 b2 c1", lines[0]);
        Assert.Equal("  FRAME [FF B3]  (0 B)", lines[1]);
        Assert.Equal(DiagVerdict.Pass, result.Verdict);
    }

    [Fact]
    public async Task ReceiveCheck_PassesWhenBytesArriveAndStopsAfterLinger()
    {
        var watch = new FakeWatchTransport([]);
        watch.InjectRaw([0x12, 0x34, 0x56]);
        var lines = new List<string>();
        var started = DateTime.UtcNow;
        var result = await ReceiveCheck.RunAsync(watch, TimeSpan.FromSeconds(30), lines.Add, null, CancellationToken.None);

        Assert.Equal(DiagVerdict.Pass, result.Verdict);
        Assert.Equal("raw: 12 34 56", lines[0]);
        // Stops about Linger after the first bytes, well before the 30 s timeout.
        Assert.InRange(DateTime.UtcNow - started, ReceiveCheck.Linger - TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReceiveCheck_FailsWithHintsWhenNothingArrives()
    {
        var result = await ReceiveCheck.RunAsync(new FakeWatchTransport([]), TimeSpan.FromMilliseconds(500), _ => { }, null, CancellationToken.None);
        Assert.Equal(DiagVerdict.Fail, result.Verdict);
        Assert.Contains("RST", result.Details);
    }

    [Fact]
    public void SelfTest_Passes() => Assert.True(SelfTest.Run());

    [Fact]
    public void PortDiscovery_DoesNotThrow()
    {
        var ports = PortDiscovery.ListPorts();
        Assert.NotNull(ports);
        Assert.Null(PortDiscovery.AutoSelect([new PortInfo("COM1", false, ""), new PortInfo("COM3", false, "")]));
        Assert.Equal("COM9", PortDiscovery.AutoSelect([new PortInfo("COM1", false, ""), new PortInfo("COM9", true, "")])?.Name);
    }

    /// <summary>Everything written comes straight back, like the GP0-GP1 jumper.</summary>
    private sealed class LoopbackTransport : ITransport
    {
        private readonly Queue<byte[]> _rx = new();

        public Task OpenAsync(CancellationToken ct) => Task.CompletedTask;

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            _rx.Enqueue(data.ToArray());
            return Task.CompletedTask;
        }

        public async Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct)
        {
            if (_rx.TryDequeue(out var chunk))
            {
                chunk.CopyTo(buffer);
                return chunk.Length;
            }
            await Task.Delay(timeout, ct);
            return 0;
        }

        public void DiscardInput() => _rx.Clear();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
