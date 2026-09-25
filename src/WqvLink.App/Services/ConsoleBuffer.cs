using System.Collections.Concurrent;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WqvLink.App.Services;

/// <summary>
/// text for an on-screen console. Lines can be added from any thread at any rate - the UI is updated
/// at most ten times a second and only the last <c>maxLines</c> are kept, so a flood of bytes
/// (a TV remote, ambient light) can't freeze the window.
/// </summary>
public sealed partial class ConsoleBuffer : ObservableObject, IDisposable
{
    private const int MaxLineLength = 240;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly Queue<string> _lines = new();
    private readonly int _maxLines;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private string _text = "";

    public ConsoleBuffer(int maxLines = 300)
    {
        _maxLines = maxLines;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Flush());
        _timer.Start();
    }

    //Queues a line, should be safe to call from any thread
    public void Add(string line) =>
        _pending.Enqueue(line.Length > MaxLineLength ? line[..MaxLineLength] + " …" : line);

    //Clears everything -> called on the UI thread
    public void Clear()
    {
        _pending.Clear();
        _lines.Clear();
        Text = "";
    }

    private void Flush()
    {
        if (_pending.IsEmpty)
        {
            return;
        }
        while (_pending.TryDequeue(out var line))
        {
            _lines.Enqueue(line);
            if (_lines.Count > _maxLines)
            {
                _lines.Dequeue();
            }
        }
        Text = string.Join(Environment.NewLine, _lines);
    }

    public void Dispose() => _timer.Stop();
}
