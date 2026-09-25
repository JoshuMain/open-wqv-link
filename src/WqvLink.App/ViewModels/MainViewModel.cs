using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WqvLink.App.Services;
using WqvLink.Core.Imaging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Storage;
using WqvLink.Core.Transport;

namespace WqvLink.App.ViewModels;

/// <summary>One photo in the gallery, with its decoded bitmap cached.</summary>
public sealed class PhotoItem(WqvImage image, Bitmap bitmap)
{
    public WqvImage Image { get; } = image;
    public Bitmap Bitmap { get; } = bitmap;

    public string Title => Image.Name.Length > 0 ? $"#{Image.Index} · {Image.Name}" : $"#{Image.Index}";

    public string DateText => Image.Taken is { } t
        ? t.ToString("d MMM yyyy  HH:mm", CultureInfo.CurrentCulture)
        : "No date";

    public string Details =>
        $"Photo {Image.Index}\nName: {(Image.Name.Length > 0 ? Image.Name : "(none)")}\nTaken: {DateText}\nDate bytes: {Hex.Format(Image.DateBytes)}";
}

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private double _thumbnailSize;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string? _currentFolder;

    [ObservableProperty]
    private string _portText = "";

    public MainViewModel(Settings settings)
    {
        Settings = settings;
        _thumbnailSize = settings.ThumbnailSize;
        Photos.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPhotos));
            OnPropertyChanged(nameof(CountText));
        };
        RefreshPortText();
    }

    public Settings Settings { get; }

    public ObservableCollection<PhotoItem> Photos { get; } = [];

    public bool HasPhotos => Photos.Count > 0;

    public string CountText => Photos.Count == 1 ? "1 photo" : $"{Photos.Count} photos";

    //The images are shown, in order
    public IReadOnlyList<WqvImage> Images => Photos.Select(p => p.Image).ToList();

    partial void OnThumbnailSizeChanged(double value) => Settings.ThumbnailSize = (int)Math.Round(value);

    public void RefreshPortText()
    {
        PortText = Settings.LastPort is { } p ? $"Pico: {p}" : "Pico: not set up";
    }

    //Replaces the gallery with <paramref name="images"/>
    public void Show(IReadOnlyList<WqvImage> images, string? folder, string description)
    {
        var decode = new DecodeOptions(Settings.SwapNibbles, Settings.Invert);
        Photos.Clear();
        foreach (var img in images)
        {
            Photos.Add(new PhotoItem(img, BitmapFactory.FromGrey8(ImageDecoder.ToGrey8(img, decode), WqvConstants.ImageSize, WqvConstants.ImageSize)));
        }
        CurrentFolder = folder;
        StatusText = description;
    }

    //Redraws every photo with the current decode options (after Options change)
    public void Rerender() => Show(Images, CurrentFolder, StatusText);

    //Loads a session folder or a raw dump file.
    public async Task LoadAsync(string dumpOrFolder)
    {
        var images = await Task.Run(() => SessionStore.LoadDump(dumpOrFolder));
        var folder = Directory.Exists(dumpOrFolder) ? dumpOrFolder : Path.GetDirectoryName(dumpOrFolder);
        Show(images, folder, $"Showing {images.Count} photo(s) from {dumpOrFolder}");
    }

    //Shows the most recent download, if there is one.
    public async Task<bool> LoadLatestAsync()
    {
        var latest = new SessionStore(Settings.OutputRoot).ListSessions().FirstOrDefault();
        if (latest is null)
        {
            return false;
        }
        await LoadAsync(latest);
        return true;
    }

    /// <summary>
    /// The port to use: 
    /// the saved one if it is plugged in, otherwise the only Pico found (which is then saved).
    /// Returns null if no Pico can be found.
    /// </summary>
    public string? ResolvePort()
    {
        var ports = PortDiscovery.ListPorts();
        if (Settings.LastPort is { } saved && ports.Any(p => string.Equals(p.Name, saved, StringComparison.OrdinalIgnoreCase)))
        {
            return saved;
        }
        if (PortDiscovery.AutoSelect(ports) is { } pico)
        {
            Settings.LastPort = pico.Name;
            SaveSettings();
            RefreshPortText();
            return pico.Name;
        }
        return null;
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Couldn't save settings: {ex.Message}";
        }
    }
}
