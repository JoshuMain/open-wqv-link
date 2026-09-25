using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WqvLink.App.Services;
using WqvLink.App.ViewModels;
using WqvLink.Core.Firmware;

namespace WqvLink.App.Views;

public sealed partial class SetupWindow : Window
{
    private CancellationTokenSource? _flashCts;
    private CancellationTokenSource? _checkCts;

    public SetupWindow()
    {
        InitializeComponent();
    }

    public SetupWindow(SetupViewModel vm) : this()
    {
        DataContext = vm;
        vm.Check.Console.PropertyChanged += ScrollCheckOutputToEnd;
        Closing += (_, e) =>
        {
            if (vm.IsBusy)
            {
                e.Cancel = true; // don't abandon a flash half-way
                return;
            }
            _checkCts?.Cancel();
        };
        Closed += (_, _) => vm.Dispose();
    }

    private SetupViewModel Vm => (SetupViewModel)DataContext!;

    // ---- 1. Find the Pico

    private void Refresh_Click(object? sender, RoutedEventArgs e) => Vm.Refresh();

    private void SavePort_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm.SavePort())
        {
            Next_Click(sender, e);
        }
    }

    private void Next_Click(object? sender, RoutedEventArgs e) =>
        Tabs.SelectedIndex = Math.Min(Tabs.SelectedIndex + 1, Tabs.ItemCount - 1);

    private void Wiring_Click(object? sender, RoutedEventArgs e) =>
        new InfoWindow("Wiring guide", HelpText.Wiring, monospace: true).Show(this);

    // ---- 2. Firmware

    private async void FlashBundled_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm.Firmware is { } fw)
        {
            await FlashAsync(fw, "WQV bridge firmware");
        }
    }

    private async void FlashFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Pico firmware",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("UF2 firmware") { Patterns = ["*.uf2"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }
        var data = await File.ReadAllBytesAsync(path);
        if (Uf2.Validate(data) is { } error)
        {
            await Dialogs.InfoAsync(this, "Not a UF2 file", $"{Path.GetFileName(path)} can't be flashed: {error}.");
            return;
        }
        await FlashAsync(data, Path.GetFileName(path));
    }

    private async Task FlashAsync(byte[] uf2, string description)
    {
        // Never flash without an explicit confirmation showing what will be written
        var target = Vm.SelectedPort is { IsPico: true } p ? $"the Pico on {p.Name}" : "the Pico";
        var ok = await Dialogs.ConfirmAsync(this, "Flash the Pico?",
            $"This will write {description} ({uf2.Length / 1024} KB, for Raspberry Pi Pico / RP2040) to {target}, replacing its current program.",
            "Flash");
        if (!ok)
        {
            return;
        }
        _flashCts = new CancellationTokenSource();
        try
        {
            await Vm.FlashAsync(uf2, _flashCts.Token);
        }
        finally
        {
            _flashCts.Dispose();
            _flashCts = null;
        }
    }

    private void CancelFlash_Click(object? sender, RoutedEventArgs e) => _flashCts?.Cancel();

    // ---- 3. Test it works

    private async void Test_Click(object? sender, RoutedEventArgs e)
    {
        _checkCts = new CancellationTokenSource();
        try
        {
            if (await Vm.Check.RunAsync(_checkCts.Token))
            {
                Close(); // set up and verified: done
            }
        }
        finally
        {
            _checkCts?.Dispose();
            _checkCts = null;
        }
    }

    private void Choice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
        {
            Vm.Check.Choose(index);
        }
    }

    private void StopPhase_Click(object? sender, RoutedEventArgs e) => Vm.Check.StopPhase();

    private void ScrollCheckOutputToEnd(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleBuffer.Text))
        {
            CheckOutput.CaretIndex = CheckOutput.Text?.Length ?? 0;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
