using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Diagnostics;

/// <summary>Full optical round trip via a mirror or white card</summary>
public static class EchoTest
{
    public const string Instructions = "Point the Click at a mirror or white card 5-10 cm away.";

    public static async Task<DiagResult> RunAsync(ITransport t, CancellationToken ct, ByteTraceLog? log = null)
    {
        var frame = Framing.Build(WqvConstants.BroadcastAddr, WqvConstants.CtrlHello);
        int hits = 0, total = 0;
        for (var i = 0; i < 20; i++)
        {
            t.DiscardInput();
            log?.Sent(frame);
            await t.WriteAsync(frame, ct).ConfigureAwait(false);
            var got = await t.ReadForAsync(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            if (got.Length > 0)
            {
                log?.Received(got);
            }
            total += got.Length;
            if (got.AsSpan().IndexOf(frame) >= 0)
            {
                hits++;
            }
        }

        var summary = $"{hits}/20 frames came back intact, {total} bytes received in total";
        if (hits > 0)
        {
            return new DiagResult(DiagVerdict.Pass, summary, "Transmit AND receive paths work (full optical round trip).");
        }
        if (total > 0)
        {
            return new DiagResult(DiagVerdict.Partial, summary, "Light is being received but corrupted - try distance/angle.");
        }
        return new DiagResult(DiagVerdict.Inconclusive, summary,
            "The transceiver may blank itself while sending. Use Blast + phone camera for TX, and Sniff + a watch/remote for RX.");
    }
}
