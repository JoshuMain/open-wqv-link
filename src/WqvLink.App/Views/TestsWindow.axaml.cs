using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WqvLink.App.Services;
using WqvLink.App.ViewModels;

namespace WqvLink.App.Views;

/// <summary>Non-modal hardware test window.</summary>
public sealed partial class TestsWindow : Window
{
    private CancellationTokenSource? _cts;

    public TestsWindow()
    {
        InitializeComponent();
    }

    public TestsWindow(TestsViewModel vm) : this()
    {
        DataContext = vm;
        vm.Console.PropertyChanged += ScrollOutputToEnd;
        Closing += (_, _) => _cts?.Cancel();
        Closed += (_, _) => vm.Dispose();
    }

    private TestsViewModel Vm => (TestsViewModel)DataContext!;

    private async void Run_Click(object? sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        try
        {
            await Vm.RunAsync(_cts.Token);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Stop_Click(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    private void ScrollOutputToEnd(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleBuffer.Text))
        {
            Output.CaretIndex = Output.Text?.Length ?? 0;
        }
    }
}
