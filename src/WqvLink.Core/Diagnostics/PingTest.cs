using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Diagnostics;

/// <summary>Handshake then disconnect</summary>
public static class PingTest
{
    public const string Instructions = "Put the watch in IR -> COM -> PC, 5-10 cm from the Click, pointing at it.";

    public static async Task<DiagResult> RunAsync(ITransport t, ByteTraceLog log, SessionOptions? opts, CancellationToken ct, IProgress<int>? helloAttempts = null)
    {
        var session = new WqvSession(t, log, opts);
        try
        {
            var info = await session.ConnectAsync(helloAttempts, ct).ConfigureAwait(false);
            await session.DisconnectAsync(ct).ConfigureAwait(false);
            return new DiagResult(DiagVerdict.Pass, "full handshake with the watch works",
                $"Watch clock {info.ClockText} ({Hex.Format(info.Clock)}), address {info.Address:X2}.");
        }
        catch (WqvLinkException ex)
        {
            return new DiagResult(DiagVerdict.Fail, ex.Message, Hints.For(ex));
        }
    }
}
