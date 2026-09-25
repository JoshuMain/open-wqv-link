using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WqvLink.App.ViewModels;
using WqvLink.Core.Imaging;
using WqvLink.Core.Storage;

namespace WqvLink.App.Views;

/// <summary>Large view of one photo with its details.</summary>
public sealed partial class PhotoWindow : Window
{
    private readonly Settings? _settings;

    public PhotoWindow()
    {
        InitializeComponent();
    }

    public PhotoWindow(PhotoItem item, Settings settings) : this()
    {
        DataContext = item;
        _settings = settings;
    }

    private PhotoItem Item => (PhotoItem)DataContext!;

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }
        var opts = _settings.ToExportOptions();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save photo",
            SuggestedFileName = FileNamer.Render(opts.FileNameTemplate, Item.Image) + opts.Extension,
            DefaultExtension = opts.Extension.TrimStart('.'),
            FileTypeChoices = [opts.Format == ImageFormat.Bmp
                ? new FilePickerFileType("BMP image") { Patterns = ["*.bmp"] }
                : FilePickerFileTypes.ImagePng],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }
        try
        {
            await File.WriteAllBytesAsync(path, ImageExporter.Encode(Item.Image, opts));
            if (opts.SetFileDates)
            {
                ImageExporter.StampFileTimes(path, Item.Image.Taken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.InfoAsync(this, "Couldn't save", ex.Message);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
