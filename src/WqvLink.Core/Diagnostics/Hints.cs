using WqvLink.Core.Protocol;

namespace WqvLink.Core.Diagnostics;

/// <summary>User-facing hints chosen by failure stage</summary>
public static class Hints
{
    public static string For(WqvLinkException ex)
    {
        if (ex.ChecksumErrors > 2)
        {
            return "Checksum errors: check alignment and distance (5-10 cm), and avoid bright ambient light.";
        }
        return ex.Stage switch
        {
            SessionStage.Hello => "No answer from the watch: check it is in IR -> COM -> PC mode, 5-10 cm away and pointing at the Click, and that RST is high.",
            SessionStage.Data => "The transfer stalled: keep the watch still and pointed at the Click. Check the battery isn't low.",
            _ => "The watch stopped answering: keep it still and close, then try again.",
        };
    }
}
