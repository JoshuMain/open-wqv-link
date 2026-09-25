using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WqvLink.App.Services;
using WqvLink.App.ViewModels;
using WqvLink.Core.Imaging;

namespace WqvLink.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel vm, string? open = null) : this()
    {
        DataContext = vm;
        Opened += async (_, _) =>
        {
            if (open is not null)
            {
                await Run(() => Vm.LoadAsync(open), "Couldn't open that file");
            }
            else if (!await Vm.LoadLatestAsync())
            {
                Vm.StatusText = "No photos downloaded yet.";
            }
        };
        Closing += (_, _) => Vm.SaveSettings();
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

    // ---- File

    private async void OpenDump_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open photos",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("WQV-1 raw dump") { Patterns = ["*.bin"] }, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await Run(() => Vm.LoadAsync(path), "Couldn't open that file");
        }
    }

    private async void OpenLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (!await Vm.LoadLatestAsync())
        {
            await Dialogs.InfoAsync(this, "No downloads", $"There are no downloads in {Vm.Settings.OutputRoot} yet.");
        }
    }

    private async void ExportSelected_Click(object? sender, RoutedEventArgs e)
    {
        var selected = Gallery.SelectedItems?.OfType<PhotoItem>().Select(p => p.Image).ToList() ?? [];
        if (selected.Count == 0)
        {
            await Dialogs.InfoAsync(this, "Export", "Select one or more photos first (Ctrl+click to select several).");
            return;
        }
        await ExportAsync(selected);
    }

    private async void ExportAll_Click(object? sender, RoutedEventArgs e)
    {
        if (!Vm.HasPhotos)
        {
            await Dialogs.InfoAsync(this, "Export", "There are no photos to export.");
            return;
        }
        await ExportAsync(Vm.Images);
    }

    private async Task ExportAsync(IReadOnlyList<WqvImage> images)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Export photos to…", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }
        var opts = Vm.Settings.ToExportOptions();
        await Run(async () =>
        {
            await Task.Run(() => ImageExporter.ExportAll(images, folder, opts));
            Vm.StatusText = $"Exported {images.Count} photo(s) to {folder}";
        }, "Export failed");
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folder = Vm.CurrentFolder ?? Vm.Settings.OutputRoot;
        Directory.CreateDirectory(folder);
        await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    // ---- Watch

    private async void Download_Click(object? sender, RoutedEventArgs e)
    {
        var port = Vm.ResolvePort();
        if (port is null)
        {
            await Dialogs.InfoAsync(this, "No Pico found",
                "Open WQV Link couldn't find the Pico.\n\nPlug it in, then use Microcontroller → Set up / flash Pico to find its port (and install the firmware if needed).");
            return;
        }
        var dlg = new DownloadWindow(new DownloadViewModel(Vm.Settings, port));
        var folder = await dlg.ShowDialog<string?>(this);
        if (folder is not null)
        {
            await Run(() => Vm.LoadAsync(folder), "Couldn't show the new photos");
        }
    }

    // ---- Microcontroller

    private async void Setup_Click(object? sender, RoutedEventArgs e)
    {
        await new SetupWindow(new SetupViewModel(Vm.Settings)).ShowDialog(this);
        Vm.RefreshPortText();
    }

    private async void FindPort_Click(object? sender, RoutedEventArgs e)
    {
        var port = Vm.ResolvePort();
        await Dialogs.InfoAsync(this, "Find Pico port", port is null
            ? "No Pico found. Check the USB cable, or open Microcontroller → Set up / flash Pico."
            : $"Using the Pico on {port}. This port has been saved.");
    }

    // ---- Tests

    private void Test_Click(object? sender, RoutedEventArgs e)
    {
        var kind = sender is MenuItem { Tag: string tag } && Enum.TryParse<TestKind>(tag, out var k) ? k : TestKind.Loopback;
        new TestsWindow(new TestsViewModel(Vm.Settings, kind)).Show(this);
    }

    // ---- Options

    private async void Options_Click(object? sender, RoutedEventArgs e)
    {
        var saved = await new OptionsWindow(new OptionsViewModel(Vm.Settings)).ShowDialog<bool>(this);
        if (saved)
        {
            Vm.Rerender();
            Vm.StatusText = "Options saved. They apply to the next download or export.";
        }
    }

    // ---- Help

    private void HelpWiring_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Wiring guide", HelpText.Wiring, monospace: true).Show(this);

    private void HelpWatch_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Using the watch", HelpText.Watch).Show(this);

    private void HelpTroubleshooting_Click(object? sender, RoutedEventArgs e) => new InfoWindow("Troubleshooting", HelpText.Troubleshooting).Show(this);

    private void HelpAbout_Click(object? sender, RoutedEventArgs e) => new InfoWindow("About Open WQV Link", HelpText.About).ShowDialog(this);

    // ---- Gallery

    private void Gallery_DoubleTapped(object? sender, TappedEventArgs e) => ViewSelected();

    private void View_Click(object? sender, RoutedEventArgs e) => ViewSelected();

    private void ViewSelected()
    {
        if (Gallery.SelectedItem is PhotoItem item)
        {
            new PhotoWindow(item, Vm.Settings).Show(this);
        }
    }

    /// <summary>Runs an action, showing any file or data error in a dialog instead of crashing.</summary>
    private async Task Run(Func<Task> action, string failureTitle)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await Dialogs.InfoAsync(this, failureTitle, ex.Message);
        }
    }
}
