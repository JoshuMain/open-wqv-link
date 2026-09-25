using System.Text.Json;
using WqvLink.Core.Imaging;
using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Tests.TestData;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Tests;

/// <summary>
/// Checks the decoder against the owner's real dumps in the git-ignored samples/ folder, using the
/// JSON sidecars the Python reference wrote for the same dump. Passes trivially when samples/ is absent
/// (for example in CI).
/// </summary>
public sealed class SamplesTests
{
    [Fact]
    public void RealDump_MatchesPythonSidecars()
    {
        var samples = Path.Combine(FixtureFactory.RepoRoot(), "samples");
        if (!Directory.Exists(samples))
        {
            return;
        }

        foreach (var dumpPath in Directory.GetFiles(samples, "*.bin"))
        {
            var images = ImageDecoder.DecodeDump(File.ReadAllBytes(dumpPath), out var trailing);
            Assert.Equal(0, trailing);

            var sidecars = Directory.GetFiles(samples, "*.json");
            if (sidecars.Length == 0)
            {
                continue;
            }
            Assert.Equal(sidecars.Length, images.Count);

            foreach (var path in sidecars)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                var img = images[root.GetProperty("index").GetInt32() - 1];
                Assert.Equal(root.GetProperty("name").GetString(), img.Name);
                Assert.Equal(root.GetProperty("stamp").GetString(), img.Stamp);
                Assert.Equal(root.GetProperty("date_bytes").EnumerateArray().Select(e => (byte)e.GetInt32()), img.DateBytes);
                Assert.Equal(Path.GetFileNameWithoutExtension(path), img.BaseFileName);
            }
        }
    }

    [Fact]
    public async Task RealDump_ReplayedThroughFakeWatchIsByteIdentical()
    {
        var samples = Path.Combine(FixtureFactory.RepoRoot(), "samples");
        if (!Directory.Exists(samples))
        {
            return;
        }

        foreach (var dumpPath in Directory.GetFiles(samples, "*.bin"))
        {
            var dump = File.ReadAllBytes(dumpPath);
            var watch = FakeWatchTransport.FromRecords(dump);
            var session = new WqvSession(watch, ByteTraceLog.Null);
            await session.ConnectAsync(CancellationToken.None);
            var result = await session.DownloadAllAsync(null, CancellationToken.None);

            Assert.Equal(dump.Length / 7229, result.ImageCount);
            Assert.Equal(dump.AsSpan(0, result.ImageCount * 7229).ToArray(), result.Raw);
            // Same close frame the real watch accepted at the end of samples/wqvlink.log.
            Assert.Contains(watch.HostFrames, f => f.Ctrl == 0xF4);
            Assert.True(watch.Disconnected);
        }
    }
}
