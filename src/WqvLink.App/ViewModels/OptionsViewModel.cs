using CommunityToolkit.Mvvm.ComponentModel;
using WqvLink.Core.Imaging;
using WqvLink.Core.Storage;

namespace WqvLink.App.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Editable copy of the settings; applied only when the user presses Save.</summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    private static readonly WqvImage Sample =
        new(12, "MY CAT", new DateTime(2024, 12, 30, 13, 50, 0), [24, 12, 30, 13, 50], new byte[7200]);

    private readonly Settings _settings;

    [ObservableProperty]
    private string _outputRoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(IsPng))]
    private Choice<ImageFormat> _format;

    [ObservableProperty]
    private Choice<int> _scale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _fileNameTemplate;

    [ObservableProperty]
    private Choice<string>? _preset;

    [ObservableProperty]
    private bool _setFileDates;

    [ObservableProperty]
    private bool _embedMetadata;

    [ObservableProperty]
    private bool _writeSidecars;

    [ObservableProperty]
    private bool _invert;

    [ObservableProperty]
    private bool _swapNibbles;

    public OptionsViewModel(Settings settings)
    {
        _settings = settings;
        _outputRoot = settings.OutputRoot;
        _format = Formats.First(f => f.Value == settings.OutputFormat);
        _scale = Scales.FirstOrDefault(s => s.Value == settings.ExportScale) ?? Scales[0];
        _fileNameTemplate = settings.FileNameTemplate;
        _setFileDates = settings.SetFileDates;
        _embedMetadata = settings.EmbedMetadata;
        _writeSidecars = settings.WriteSidecars;
        _invert = settings.Invert;
        _swapNibbles = settings.SwapNibbles;
    }

    public IReadOnlyList<Choice<ImageFormat>> Formats { get; } =
    [
        new(ImageFormat.Png, "PNG (recommended, keeps name and date inside the file)"),
        new(ImageFormat.Bmp, "BMP (uncompressed)"),
    ];

    public IReadOnlyList<Choice<int>> Scales { get; } =
    [
        new(1, "1× (120 × 120, original)"),
        new(2, "2× (240 × 240)"),
        new(4, "4× (480 × 480)"),
        new(8, "8× (960 × 960)"),
    ];

    public IReadOnlyList<Choice<string>> Presets { get; } =
        FileNamer.Presets.Select(p => new Choice<string>(p.Template, p.Label)).ToList();

    public bool IsPng => Format.Value == ImageFormat.Png;

    public string Preview => "Example: " + FileNamer.Render(FileNameTemplate, Sample) + (Format.Value == ImageFormat.Bmp ? ".bmp" : ".png");

    partial void OnPresetChanged(Choice<string>? value)
    {
        if (value is not null)
        {
            FileNameTemplate = value.Value;
        }
    }

    public void ResetToDefaults()
    {
        var d = new Settings();
        OutputRoot = d.OutputRoot;
        Format = Formats[0];
        Scale = Scales[0];
        FileNameTemplate = d.FileNameTemplate;
        SetFileDates = d.SetFileDates;
        EmbedMetadata = d.EmbedMetadata;
        WriteSidecars = d.WriteSidecars;
        Invert = d.Invert;
        SwapNibbles = d.SwapNibbles;
    }

    public void Apply()
    {
        _settings.OutputRoot = string.IsNullOrWhiteSpace(OutputRoot) ? SessionStore.DefaultRoot : OutputRoot.Trim();
        _settings.OutputFormat = Format.Value;
        _settings.ExportScale = Scale.Value;
        _settings.FileNameTemplate = string.IsNullOrWhiteSpace(FileNameTemplate) ? FileNamer.DefaultTemplate : FileNameTemplate.Trim();
        _settings.SetFileDates = SetFileDates;
        _settings.EmbedMetadata = EmbedMetadata;
        _settings.WriteSidecars = WriteSidecars;
        _settings.Invert = Invert;
        _settings.SwapNibbles = SwapNibbles;
        _settings.Save();
    }
}
