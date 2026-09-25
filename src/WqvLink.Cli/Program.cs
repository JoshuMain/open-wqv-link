using System.Globalization;
using WqvLink.Core.Diagnostics;
using WqvLink.Core.Imaging;
using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Storage;
using WqvLink.Core.Transport;

namespace WqvLink.Cli;

/// <summary>
/// Basic console front-end over the UI
/// </summary>
internal static class Program
{
    private const string Usage = """
        wqvlink - Casio WQV-1 wrist camera downloader (Pico + IrDA 3 Click)

        usage: wqvlink [-p PORT] [-v] [--log FILE] <command> [options]

        Bench tests (no watch needed):
          loopback                 Click unplugged, GP0 jumpered to GP1
          blast [--seconds N]      stream IR pulses; look with a phone camera (default 10 s)
          echo                     point the Click at a mirror / white card
          sniff                    print everything the Click receives (Ctrl+C to stop)

        With the watch (IR mode -> COM -> PC):
          ping [--assign ADDR]     handshake only
          download [--out ROOT] [--expect N] [--swap-nibbles] [--invert]
                   [--assign ADDR] [--scale 1|2|4|8] [--no-sidecars]
                                   all images -> ROOT/yyyyMMdd-HHmmss/

        Offline:
          decode FILE [--out DIR] [--swap-nibbles] [--invert] [--scale N] [--no-sidecars]
          list-ports
          selftest

        Options:
          -p, --port PORT          serial port, e.g. COM5 or /dev/ttyACM0 (default: the only Pico found)
          -v, --verbose            print every byte
          --log FILE               raw trace log (default: session.log in the session folder for
                                   download, otherwise wqvlink.log)
          --fake-dump FILE         simulate a watch holding the images in FILE (no hardware)
        """;

    private static async Task<int> Main(string[] argv)
    {
        Args args;
        try
        {
            args = Args.Parse(argv);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}\n");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (args.Command is null or "help" || args.Flag("help") || args.Flag("h"))
        {
            Console.WriteLine(Usage);
            return args.Command is null ? 2 : 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return args.Command switch
            {
                "selftest" => SelfTestCommand(),
                "list-ports" => ListPorts(),
                "decode" => Decode(args),
                "loopback" or "blast" or "echo" or "sniff" or "ping" or "download" => await RunWithTransport(args, cts.Token),
                _ => throw new ArgumentException($"unknown command '{args.Command}'"),
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (TransportException ex)
        {
            Console.Error.WriteLine($"\nFAILED: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nCancelled.");
            return 130;
        }
    }

    private static int SelfTestCommand()
    {
        var ok = SelfTest.Run();
        Console.WriteLine(ok ? "selftest OK" : "selftest FAILED");
        return ok ? 0 : 1;
    }

    private static int ListPorts()
    {
        var ports = PortDiscovery.ListPorts();
        if (ports.Count == 0)
        {
            Console.WriteLine("No serial ports found.");
            return 1;
        }
        foreach (var p in ports)
        {
            Console.WriteLine(p.IsPico ? $"{p.Name,-16} Pico (recommended)" : p.Name);
        }
        return 0;
    }

    private static int Decode(Args args)
    {
        var file = args.Positional.FirstOrDefault() ?? throw new ArgumentException("decode needs a FILE");
        var outDir = args.Value("out") ?? "wqv_out";
        var images = ImageDecoder.DecodeDump(File.ReadAllBytes(file), out var trailing);
        if (trailing > 0)
        {
            Console.WriteLine($"  note: {trailing} trailing bytes ignored");
        }
        foreach (var path in SessionStore.ExportAll(images, outDir, ExportOptionsFrom(args)))
        {
            Console.WriteLine($"  saved {Path.GetFileName(path)}");
        }
        Console.WriteLine($"Done: {images.Count} image(s) in {outDir}");
        return 0;
    }

    private static async Task<int> RunWithTransport(Args args, CancellationToken ct)
    {
        string? sessionFolder = null;
        if (args.Command == "download")
        {
            var root = args.Value("out") ?? Settings.Load().OutputRoot;
            sessionFolder = new SessionStore(root).CreateSessionFolder(DateTime.Now);
        }

        var logPath = args.Value("log")
            ?? (sessionFolder is not null ? Path.Combine(sessionFolder, SessionStore.LogFileName) : "wqvlink.log");
        using var log = new ByteTraceLog(logPath);
        if (args.Flag("v") || args.Flag("verbose"))
        {
            log.Line += Console.WriteLine;
        }

        await using var transport = OpenTransport(args);
        await transport.OpenAsync(ct);

        try
        {
            switch (args.Command)
            {
                case "loopback":
                    Console.WriteLine(LoopbackTest.Instructions);
                    return Report(await LoopbackTest.RunAsync(transport, ct, log));
                case "blast":
                    var seconds = double.Parse(args.Value("seconds") ?? "10", CultureInfo.InvariantCulture);
                    Console.WriteLine($"Sending 0x00 bytes for {seconds}s. {BlastTest.Instructions}");
                    return Report(await BlastTest.RunAsync(transport, TimeSpan.FromSeconds(seconds), ct));
                case "echo":
                    Console.WriteLine(EchoTest.Instructions);
                    return Report(await EchoTest.RunAsync(transport, ct, log));
                case "sniff":
                    Console.WriteLine($"Listening (Ctrl+C to stop). {Sniffer.Instructions}");
                    return Report(await Sniffer.RunAsync(transport, Console.WriteLine, ct));
                case "ping":
                    Console.WriteLine($"Waiting for the watch... {PingTest.Instructions}");
                    return Report(await PingTest.RunAsync(transport, log, SessionOptionsFrom(args), ct, HelloCounter()));
                default:
                    return await Download(args, transport, log, sessionFolder!, ct);
            }
        }
        catch (WqvLinkException ex)
        {
            Console.Error.WriteLine($"\nFAILED: {ex.Message}\n  {Hints.For(ex)}\n  full byte trace in {logPath}");
            return 1;
        }
    }

    private static async Task<int> Download(Args args, ITransport transport, ByteTraceLog log, string folder, CancellationToken ct)
    {
        var session = new WqvSession(transport, log, SessionOptionsFrom(args));
        Console.WriteLine($"Waiting for the watch... {PingTest.Instructions}");
        var info = await session.ConnectAsync(HelloCounter(), ct);
        Console.WriteLine($"\n  watch answered: clock {info.ClockText}, address {info.Address:X2}");

        var announced = false;
        var result = await session.DownloadAllAsync(new ConsoleProgress<DownloadProgress>(p =>
        {
            if (!announced)
            {
                Console.WriteLine($"  {p.ImageCount} photo(s) found");
                announced = true;
            }
            var eta = p.Eta is { } e ? $", {e:mm\\:ss} left" : "";
            Console.Write($"\r  {p.BytesReceived}/{p.BytesTotal} bytes, {p.Packets} packets, {p.BytesPerSecond / 1024:0.0} KiB/s{eta}   ");
        }), ct);
        Console.WriteLine();

        var paths = SessionStore.SaveDownload(folder, result, ExportOptionsFrom(args));
        Console.WriteLine($"  raw dump saved to {Path.Combine(folder, SessionStore.DumpFileName)} ({result.Raw.Length + result.Extra.Length} bytes)");
        foreach (var path in paths)
        {
            Console.WriteLine($"  saved {Path.GetFileName(path)}");
        }
        Console.WriteLine($"Done: {paths.Count} image(s) in {folder}");
        return 0;
    }

    private static ITransport OpenTransport(Args args)
    {
        if (args.Value("fake-dump") is { } fake)
        {
            var watch = FakeWatchTransport.FromRecords(File.ReadAllBytes(fake));
            Console.WriteLine($"(simulated watch replaying {fake})");
            return watch;
        }

        var port = args.Value("p") ?? args.Value("port");
        if (port is null)
        {
            var picked = PortDiscovery.AutoSelect(PortDiscovery.ListPorts())
                ?? throw new ArgumentException("--port is required (no single Pico found; try 'list-ports')");
            port = picked.Name;
            Console.WriteLine($"Using {port}");
        }
        return new SerialTransport(port);
    }

    private static SessionOptions SessionOptionsFrom(Args args)
    {
        var opts = Settings.Load().Protocol.ToSessionOptions();
        if (args.Value("assign") is { } assign)
        {
            opts = opts with { AssignedAddress = ParseByte(assign) };
        }
        if (args.Value("expect") is { } expect)
        {
            opts = opts with { ExpectedCount = int.Parse(expect, CultureInfo.InvariantCulture) };
        }
        return opts;
    }

    private static ExportOptions ExportOptionsFrom(Args args)
    {
        var scale = int.Parse(args.Value("scale") ?? "1", CultureInfo.InvariantCulture);
        if (scale is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentException("--scale must be 1, 2, 4 or 8");
        }
        // Sidecars are on by default in the CLI for parity with the Python reference.
        return new ExportOptions
        {
            Decode = new DecodeOptions(args.Flag("swap-nibbles"), args.Flag("invert")),
            Scale = scale,
            Sidecars = !args.Flag("no-sidecars"),
        };
    }

    private static byte ParseByte(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? byte.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.Parse(s, CultureInfo.InvariantCulture);

    private static IProgress<int> HelloCounter() =>
        new ConsoleProgress<int>(n => Console.Write($"\r  waiting for watch... ({n} tries)"));

    private static int Report(DiagResult r)
    {
        Console.WriteLine(r);
        return r.Verdict is DiagVerdict.Fail ? 1 : 0;
    }

    /// <summary>Reports synchronously, so console output stays in order.</summary>
    private sealed class ConsoleProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>Minimal argument parser: one command, positionals, <c>--name value</c>, <c>--flag</c> and <c>-p VALUE</c>.</summary>
internal sealed class Args
{
    private static readonly HashSet<string> Valued = ["p", "port", "log", "seconds", "assign", "out", "expect", "scale", "fake-dump"];

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public string? Command { get; private set; }
    public List<string> Positional { get; } = [];

    public string? Value(string name) => _values.GetValueOrDefault(name);

    public bool Flag(string name) => _flags.Contains(name);

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                var name = arg.TrimStart('-');
                string? inline = null;
                var eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    inline = name[(eq + 1)..];
                    name = name[..eq];
                }
                if (Valued.Contains(name))
                {
                    var value = inline ?? (i + 1 < argv.Length ? argv[++i] : throw new ArgumentException($"{arg} needs a value"));
                    a._values[name] = value;
                }
                else
                {
                    a._flags.Add(name);
                }
            }
            else if (a.Command is null)
            {
                a.Command = arg;
            }
            else
            {
                a.Positional.Add(arg);
            }
        }
        return a;
    }
}
