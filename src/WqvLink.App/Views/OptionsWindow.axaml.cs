using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WqvLink.App.ViewModels;

namespace WqvLink.App.Views;

/// <summary>Options dialog. Returns true if the user saved.</summary>
public sealed partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    public OptionsWindow(OptionsViewModel vm) : this()
    {
        DataContext = vm;
    }

    private OptionsViewModel Vm => (OptionsViewModel)DataContext!;

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Save photos in…",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            Vm.OutputRoot = path;
        }
    }

    private void Reset_Click(object? sender, RoutedEventArgs e) => Vm.ResetToDefaults();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Vm.Apply();
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.InfoAsync(this, "Couldn't save options", ex.Message);
        }
    }
}
