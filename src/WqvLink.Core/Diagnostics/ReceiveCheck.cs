using System.Diagnostics;
using WqvLink.Core.Protocol;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Diagnostics;

/// <summary>
/// Setup wizard receive check - listens for any IR bytes, for example from a TV remote.
/// Remote-control signals aren't IrDA, so the bytes are meaningless - any at all prove the receiver works.
/// </summary>
public static class ReceiveCheck
{
    //How long to keep listening after the first bytes, so the user sees a few lines
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(3);

    public const string FailHints =
        "Check that RST is connected to GP3 (or tied to 3V3): if RST is low the Click is held in reset. " +
        "Check the Click's TX goes to GP1, JP1 is set to 3V3, and 3V3 and GND are connected.";

    public static async Task<DiagResult> RunAsync(ITransport t, TimeSpan timeout, Action<string> output,
        IProgress<TimeSpan>? remaining, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var parser = new FrameParser();
        var clock = Stopwatch.StartNew();
        TimeSpan? firstAt = null;
        long bytes = 0;
        var frames = 0;

        while (clock.Elapsed < timeout && (firstAt is null || clock.Elapsed - firstAt < Linger))
        {
            remaining?.Report(timeout - clock.Elapsed);
            var n = await t.ReadAsync(buffer, TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            if (n == 0)
            {
                continue;
            }
            firstAt ??= clock.Elapsed;
            bytes += n;
            output($"raw: {Hex.Format(buffer.AsSpan(0, n))}");
            foreach (var f in parser.Feed(buffer.AsSpan(0, n)))
            {
                frames++;
                output($"  FRAME {f}");
            }
        }

        if (bytes == 0)
        {
            return new DiagResult(DiagVerdict.Fail, $"nothing received in {timeout.TotalSeconds:0} s", FailHints);
        }
        var framesText = frames > 0 ? $", including {frames} valid IrDA frame(s)" : "";
        return new DiagResult(DiagVerdict.Pass, $"received {bytes} bytes{framesText}", "The receiver works.");
    }
}
