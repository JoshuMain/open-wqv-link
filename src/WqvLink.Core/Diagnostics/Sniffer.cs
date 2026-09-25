using WqvLink.Core.Protocol;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Diagnostics;

/// <summary>Prints everything received, raw and as decoded frames, until cancelled</summary>
public static class Sniffer
{
    public const string Instructions =
        "Try the watch in IR -> COM -> OTHERS -> SEND, or a TV remote (garbage bytes mean the receiver is alive, not valid IrDA).";

    public static async Task<DiagResult> RunAsync(ITransport t, Action<string> output, CancellationToken ct)
    {
        var parser = new FrameParser();
        var buffer = new byte[4096];
        long bytes = 0;
        var frames = 0;
        try
        {
            while (true)
            {
                var n = await t.ReadAsync(buffer, TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    continue;
                }
                bytes += n;
                output($"raw: {Hex.Format(buffer.AsSpan(0, n))}");
                foreach (var f in parser.Feed(buffer.AsSpan(0, n)))
                {
                    frames++;
                    output($"  FRAME {f}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        return bytes > 0
            ? new DiagResult(DiagVerdict.Pass, $"received {bytes} bytes, {frames} valid frame(s)", "The receiver is alive.")
            : new DiagResult(DiagVerdict.Inconclusive, "nothing received", "Check RST, the TX/RX crossover and JP1.");
    }
}
