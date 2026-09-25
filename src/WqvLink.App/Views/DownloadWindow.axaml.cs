using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WqvLink.App.ViewModels;

namespace WqvLink.App.Views;

/// <summary>
/// Modal download dialog. Shows how to get the watch ready; nothing is sent until the user presses Start.
/// Returns the session folder, or null.
/// </summary>
public sealed partial class DownloadWindow : Window
{
    private CancellationTokenSource? _cts;
    private Task? _run;
    private string? _result;

    public DownloadWindow()
    {
        InitializeComponent();
    }

    public DownloadWindow(DownloadViewModel vm) : this()
    {
        DataContext = vm;
        Closing += (_, e) =>
        {
            // Cancel must always work: stop the transfer, then close once it has wound down.
            if (_run is { IsCompleted: false })
            {
                e.Cancel = true;
                _cts?.Cancel();
                _ = CloseWhenFinishedAsync();
            }
        };
    }

    private DownloadViewModel Vm => (DownloadViewModel)DataContext!;

    private async void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (_run is { IsCompleted: false })
        {
            return;
        }
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var run = Vm.RunAsync(_cts.Token);
        _run = run;
        _result = await run;
        if (_result is not null)
        {
            Close(_result);
        }
    }

    private async Task CloseWhenFinishedAsync()
    {
        if (_run is not null)
        {
            await _run;
        }
        Close(_result);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (_run is { IsCompleted: false })
        {
            _cts?.Cancel();
        }
        else
        {
            Close(_result);
        }
    }

    private async void ShowLog_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm.LogPath is { } path && File.Exists(path))
        {
            await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }
}
