using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WqvLink.App.Services;
using WqvLink.Core.Diagnostics;
using WqvLink.Core.Logging;
using WqvLink.Core.Storage;
using WqvLink.Core.Transport;

namespace WqvLink.App.ViewModels;

/// <summary>
/// The guided "Test it works" check in Pico setup: can it send (phone camera),
/// can it receive (TV remote), and optionally can it talk to the watch.
/// Written as one linear async flow - the user's button presses complete <see cref="AskAsync"/>.
/// </summary>
public sealed partial class HardwareCheckViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan BlastTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ListenTime = TimeSpan.FromSeconds(20);

    private readonly Settings _settings;
    private TaskCompletionSource<int>? _choice;
    private CancellationTokenSource? _phaseCts;

    [ObservableProperty]
    private string _stepTitle = "Check your hardware";

    [ObservableProperty]
    private string _instructions =
        "This walks you through two quick checks, then an optional one with the watch:\n\n" +
        "  1. Can it send? You'll look at the Click through your phone's camera.\n" +
        "  2. Can it receive? You'll press buttons on a TV remote pointed at the Click.\n" +
        "  3. Can it talk to the watch? (optional)\n\n" +
        "Have your phone and a TV remote to hand, then press Test.";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _explanation = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private bool _showConsole;

    [ObservableProperty]
    private string? _choice1;

    [ObservableProperty]
    private string? _choice2;

    [ObservableProperty]
    private string? _choice3;

    public HardwareCheckViewModel(Settings settings)
    {
        _settings = settings;
    }

    public ConsoleBuffer Console { get; } = new();

    //One line per completed step, shown as a summary.
    public ObservableCollection<string> Results { get; } = [];

    //Runs the whole check. Returns true if the user pressed Finish
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        IsRunning = true;
        Results.Clear();
        Console.Clear();
        try
        {
            var port = ResolvePort();
            if (port is null)
            {
                StepTitle = "No Pico found";
                Instructions = "Plug the Pico in, then go back to \"1. Find the Pico\" and save its port.";
                await AskAsync(ct, "OK");
                return false;
            }

            await TransmitStepAsync(port, ct);
            var received = await ReceiveStepAsync(port, ct);
            await WatchStepAsync(port, ct);

            StepTitle = received ? "All done: your Pico and Click are ready" : "Finished, but something needs fixing";
            Instructions = received
                ? "Close this window, put the watch in IR → COM → PC and press \"Get photos from watch\"."
                : "The receive check didn't pass, so photos can't be downloaded yet. Check the wiring (Help → Wiring guide) and run the test again.";
            Status = "";
            Explanation = "";
            ShowConsole = false;
            if (received)
            {
                _settings.SetupCompleted = true;
                _settings.VerifiedPort = port;
                _settings.LastPort = port;
                _settings.Save();
            }
            return await AskAsync(ct, received ? "Finish" : "Close") == 0 && received;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            IsRunning = false;
            CanStop = false;
            ClearChoices();
            if (Results.Count == 0)
            {
                StepTitle = "Check your hardware";
            }
        }
    }

    //User choice
    public void Choose(int index) => _choice?.TrySetResult(index);

    //End early
    public void StopPhase() => _phaseCts?.Cancel();


    // ---- Step 1: transmit

    private async Task TransmitStepAsync(string port, CancellationToken ct)
    {
        StepTitle = "Step 1 of 3: Can it send?";
        Instructions =
            "Open your phone's camera. The front (selfie) camera works best, because many rear cameras filter infrared.\n\n" +
            "Point it at the Click's IR window from about 10 cm. When you press Start sending, the Click flashes " +
            $"its infrared LED for {BlastTime.TotalSeconds:0} seconds. Your eyes can't see it, but the camera shows a faint purple or white flicker.";
        Explanation = "";
        ShowConsole = false;

        if (await AskAsync(ct, "Start sending", "Skip") == 1)
        {
            Results.Add("–  Send: skipped");
            return;
        }

        while (true)
        {
            var sent = await RunPhaseAsync(ct, async phase =>
            {
                await using var t = await OpenAsync(port, phase);
                for (var left = (int)BlastTime.TotalSeconds; left > 0; left--)
                {
                    Status = $"Sending infrared… {left} s left. Watch the Click through your phone.";
                    await BlastTest.RunAsync(t, TimeSpan.FromSeconds(1), phase);
                    phase.ThrowIfCancellationRequested();
                }
            });
            if (!sent)
            {
                if (await AskAsync(ct, "Try again", "Skip") == 1)
                {
                    Results.Add("✗  Send: couldn't talk to the Pico. " + Status);
                    return;
                }
                continue;
            }
            Status = "Did you see it flicker on your phone's screen?";
            var answer = await AskAsync(ct, "Yes, I saw it", "No, nothing", "Send again");
            if (answer == 2)
            {
                continue;
            }
            Status = "";
            Results.Add(answer == 0
                ? "✓  Send: the infrared LED works."
                : "?  Send: not seen. Some phones filter infrared, so this isn't conclusive. If step 2 passes, the Click is powered and wired.");
            return;
        }
    }

    // ---- Step 2: receive

    private async Task<bool> ReceiveStepAsync(string port, CancellationToken ct)
    {
        StepTitle = "Step 2 of 3: Can it receive?";
        Instructions =
            "Grab a TV remote (any brand). Press Start listening, then point the remote at the Click from about " +
            "10 cm and press a few buttons.";
        Explanation =
            "What you'll see: each \"raw:\" line is bytes the Click decoded from the infrared light. A TV remote doesn't " +
            "speak IrDA, so the bytes look random, and that's expected. Any bytes at all prove that the receiver, the RST " +
            "wire and the Click-TX → GP1 wire all work.";
        Console.Clear();
        ShowConsole = true;

        if (await AskAsync(ct, "Start listening", "Skip") == 1)
        {
            Results.Add("–  Receive: skipped");
            return false;
        }

        while (true)
        {
            Console.Clear();
            DiagResult? result = null;
            var remaining = new Progress<TimeSpan>(r =>
                Status = $"Listening… {Math.Max(0, (int)Math.Ceiling(r.TotalSeconds))} s left. Press buttons on the remote.");
            var ran = await RunPhaseAsync(ct, async phase =>
            {
                await using var t = await OpenAsync(port, phase);
                result = await ReceiveCheck.RunAsync(t, ListenTime, Console.Add, remaining, phase);
            });

            if (result is { Verdict: DiagVerdict.Pass })
            {
                Status = $"Received! {result.Message}.";
                Results.Add($"✓  Receive: {result.Message}. The receiver works.");
                await AskAsync(ct, "Next");
                return true;
            }

            if (ran)
            {
                Status = result is null ? "Stopped." : $"Nothing received. {ReceiveCheck.FailHints}";
            }
            if (await AskAsync(ct, "Try again", "Continue anyway") == 1)
            {
                Results.Add("✗  Receive: nothing received. " + ReceiveCheck.FailHints);
                return false;
            }
        }
    }

    // ---- Step 3: the watch (optional)

    private async Task WatchStepAsync(string port, CancellationToken ct)
    {
        StepTitle = "Step 3 of 3: Can it talk to the watch? (optional)";
        Instructions =
            "On the watch, press MODE until you reach IR, then choose COM → PC. Hold it 5–10 cm from the Click, " +
            "IR windows facing each other, then press Ping watch.";
        Explanation =
            "What you'll see: \">\" lines are what the Pico sends, \"<\" lines are the watch's replies. " +
            "The app says hello until the watch answers, reads its clock, then says goodbye.";
        Status = "";
        Console.Clear();
        ShowConsole = true;

        while (true)
        {
            if (await AskAsync(ct, "Ping watch", "Skip") == 1)
            {
                Results.Add("–  Watch: skipped");
                return;
            }

            Console.Clear();
            DiagResult? result = null;
            var opts = _settings.Protocol.ToSessionOptions() with { HelloTries = 75 }; // about 15 s
            var tries = new Progress<int>(n => Status = $"Waiting for the watch… (try {n} of {opts.HelloTries})");
            var ran = await RunPhaseAsync(ct, async phase =>
            {
                await using var t = await OpenAsync(port, phase);
                using var log = new ByteTraceLog();
                log.Line += Console.Add;
                result = await PingTest.RunAsync(t, log, opts, phase, tries);
            });

            if (result is { Verdict: DiagVerdict.Pass })
            {
                Status = $"The watch answered. {result.Details}";
                Results.Add($"✓  Watch: connected. {result.Details}");
                await AskAsync(ct, "Next");
                return;
            }
            if (ran)
            {
                Status = result is null
                    ? "Stopped."
                    : $"No answer from the watch. Check it's in IR → COM → PC, close and pointing at the Click. {result.Message}";
            }
            Instructions = "You can try again, or skip this: you can always try the watch from the main window.";
        }
    }

    // ---- helpers

    private string? ResolvePort()
    {
        var ports = PortDiscovery.ListPorts();
        if (_settings.LastPort is { } saved && ports.Any(p => string.Equals(p.Name, saved, StringComparison.OrdinalIgnoreCase)))
        {
            return saved;
        }
        return PortDiscovery.AutoSelect(ports)?.Name;
    }

    private static async Task<SerialTransport> OpenAsync(string port, CancellationToken ct)
    {
        var t = new SerialTransport(port);
        await t.OpenAsync(ct);
        return t;
    }


    private async Task<bool> RunPhaseAsync(CancellationToken ct, Func<CancellationToken, Task> phase)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _phaseCts = cts;
        CanStop = true;
        try
        {
            await phase(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Stopped by the user - move on
            return true;
        }
        catch (Exception ex) when (ex is TransportException or IOException or UnauthorizedAccessException)
        {
            Status = $"Problem talking to the Pico: {ex.Message}";
            return false;
        }
        finally
        {
            CanStop = false;
            _phaseCts = null;
        }
    }

    private async Task<int> AskAsync(CancellationToken ct, params string[] options)
    {
        Choice1 = options.ElementAtOrDefault(0);
        Choice2 = options.ElementAtOrDefault(1);
        Choice3 = options.ElementAtOrDefault(2);
        _choice = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reg = ct.Register(() => _choice.TrySetCanceled());
        try
        {
            return await _choice.Task;
        }
        finally
        {
            ClearChoices();
        }
    }

    private void ClearChoices()
    {
        Choice1 = Choice2 = Choice3 = null;
    }

    public void Dispose() => Console.Dispose();
}
