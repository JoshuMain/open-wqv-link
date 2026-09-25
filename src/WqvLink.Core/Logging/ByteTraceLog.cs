using System.Diagnostics;
using System.Globalization;
using WqvLink.Core.Protocol;

namespace WqvLink.Core.Logging;

/// <summary>
/// Byte-level trace in the format: <c>  12.345 &gt; c0 ff b3 01 b2 c1</c>.
///Direction is <c>&gt;</c> sent, <c>&lt;</c> received, <c>!</c> note. Timestamps are monotonic seconds
/// </summary>
public sealed class ByteTraceLog : IDisposable
{
    private readonly object _lock = new();
    private readonly Func<double> _clock;
    private StreamWriter? _file;

    public ByteTraceLog(string? path = null, Func<double>? clock = null)
    {
        _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        if (path is not null)
        {
            AttachFile(path);
        }
    }

    //A log that only raises cref="Line" and writes nowhere
    public static ByteTraceLog Null => new();

    //Raised for every line written (for the Diagnostics console or -v output)
    public event Action<string>? Line;

    public string? FilePath { get; private set; }

    //Starts (or switches) appending to path
    public void AttachFile(string path)
    {
        lock (_lock)
        {
            _file?.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            FilePath = path;
        }
    }

    public void Sent(ReadOnlySpan<byte> data) => Write('>', Hex.Format(data));

    public void Received(ReadOnlySpan<byte> data) => Write('<', Hex.Format(data));

    public void Note(string message) => Write('!', message);

    public void Write(char direction, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{_clock(),10:F3} {direction} {message}");
        lock (_lock)
        {
            _file?.WriteLine(line);
        }
        Line?.Invoke(line);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _file?.Dispose();
            _file = null;
        }
    }
}
