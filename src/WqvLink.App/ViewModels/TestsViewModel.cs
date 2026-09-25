using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WqvLink.App.Services;
using WqvLink.Core.Diagnostics;
using WqvLink.Core.Logging;
using WqvLink.Core.Storage;
using WqvLink.Core.Transport;

namespace WqvLink.App.ViewModels;

public enum TestKind
{
    Loopback, //Wire check
    Blast, //Camera check for  purple light
    Echo, //Check for reflected signals - Not the best
    Sniff, //Check with tv remote, best one
    Ping, //Check if the watch is alive 
}

public sealed record TestInfo(TestKind Kind, string Name, string Instructions)
{
    public override string ToString() => Name;
}

//The hardware tests
public sealed partial class TestsViewModel : ObservableObject, IDisposable
{
    private readonly Settings _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlast))]
    private TestInfo _selectedTest;

    [ObservableProperty]
    private decimal _blastSeconds = 10;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _resultText = "";

    [ObservableProperty]
    private IBrush _resultBrush = Brushes.Gray;

    public TestsViewModel(Settings settings, TestKind initial)
    {
        _settings = settings;
        _selectedTest = Tests.First(t => t.Kind == initial);
    }

    public IReadOnlyList<TestInfo> Tests { get; } =
    [
        new(TestKind.Loopback, "Loopback (Pico only)",
            "Checks USB, the firmware and the Pico's UART.\n" + LoopbackTest.Instructions),
        new(TestKind.Blast, "Blast IR (transmitter)",
            "Sends IR continuously. " + BlastTest.Instructions + "\nSome phones filter IR, so not seeing it isn't conclusive."),
        new(TestKind.Echo, "Echo (transmit and receive)",
            "Sends 20 frames and listens for their reflection. " + EchoTest.Instructions),
        new(TestKind.Sniff, "Sniff (receiver)",
            "Shows everything the Click receives until you press Stop.\n" +
            "Point a TV remote at the Click and press a button: any bytes mean the receiver works.\n" + Sniffer.Instructions),
        new(TestKind.Ping, "Ping watch (handshake)",
            "Connects to the watch, shows its clock, then disconnects.\n" + PingTest.Instructions),
    ];

    public bool IsBlast => SelectedTest.Kind == TestKind.Blast;

    //Test output. batched so a flood of received bytes can't freeze the window
    public ConsoleBuffer Console { get; } = new();

    public async Task RunAsync(CancellationToken ct)
    {
        Console.Clear();
        ResultText = "Running…";
        ResultBrush = Brushes.Gray;
        IsRunning = true;
        try
        {
            var port = _settings.LastPort;
            if (port is null || !PortDiscovery.ListPorts().Any(p => string.Equals(p.Name, port, StringComparison.OrdinalIgnoreCase)))
            {
                port = PortDiscovery.AutoSelect(PortDiscovery.ListPorts())?.Name;
            }
            if (port is null)
            {
                Show(new DiagResult(DiagVerdict.Fail, "No Pico found.", "Open Microcontroller → Set up / flash Pico first."));
                return;
            }

            Console.Add($"Using {port}");
            using var log = new ByteTraceLog();
            log.Line += Console.Add;
            await using var t = new SerialTransport(port);
            await t.OpenAsync(ct);

            var result = SelectedTest.Kind switch
            {
                TestKind.Loopback => await LoopbackTest.RunAsync(t, ct, log),
                TestKind.Blast => await BlastTest.RunAsync(t, TimeSpan.FromSeconds((double)BlastSeconds), ct),
                TestKind.Echo => await EchoTest.RunAsync(t, ct, log),
                TestKind.Sniff => await Sniffer.RunAsync(t, Console.Add, ct),
                _ => await PingTest.RunAsync(t, log, _settings.Protocol.ToSessionOptions(), ct),
            };
            Show(result);
        }
        catch (OperationCanceledException)
        {
            ResultText = "Stopped.";
            ResultBrush = Brushes.Gray;
        }
        catch (Exception ex) when (ex is TransportException or IOException or UnauthorizedAccessException)
        {
            Show(new DiagResult(DiagVerdict.Fail, ex.Message));
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Show(DiagResult r)
    {
        ResultText = r.Details.Length > 0 ? $"{r.Verdict.ToString().ToUpperInvariant()}: {r.Message}\n{r.Details}" : $"{r.Verdict.ToString().ToUpperInvariant()}: {r.Message}";
        ResultBrush = r.Verdict switch
        {
            DiagVerdict.Pass => Brushes.SeaGreen,
            DiagVerdict.Fail => Brushes.IndianRed,
            DiagVerdict.Partial or DiagVerdict.Inconclusive => Brushes.DarkOrange,
            _ => Brushes.SteelBlue,
        };
    }

    public void Dispose() => Console.Dispose();
}
