using System.Text.Json;
using System.Text.Json.Serialization;
using WqvLink.Core.Imaging;
using WqvLink.Core.Protocol;

namespace WqvLink.Core.Storage;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>User settings, stored as JSON in <c>&lt;ApplicationData&gt;/WqvLink/settings.json</c>.</summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string OutputRoot { get; set; } = SessionStore.DefaultRoot;
    public string? LastPort { get; set; }
    public bool AutoSelectPico { get; set; } = true;
    public bool SwapNibbles { get; set; }
    public bool Invert { get; set; }
    public int ExportScale { get; set; } = 1;
    public ImageFormat OutputFormat { get; set; } = ImageFormat.Png;
    public string FileNameTemplate { get; set; } = FileNamer.DefaultTemplate;
    public bool SetFileDates { get; set; } = true;
    public bool EmbedMetadata { get; set; } = true;
    public bool WriteSidecars { get; set; }
    public int ThumbnailSize { get; set; } = 180;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public bool SetupCompleted { get; set; }
    public string? VerifiedPort { get; set; }
    public ProtocolSettings Protocol { get; set; } = new();

    //export options built from these settings
    public ExportOptions ToExportOptions() => new()
    {
        Decode = new DecodeOptions(SwapNibbles, Invert),
        Format = OutputFormat,
        Scale = ExportScale,
        FileNameTemplate = FileNameTemplate,
        SetFileDates = SetFileDates,
        EmbedMetadata = EmbedMetadata,
        Sidecars = WriteSidecars,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WqvLink", "settings.json");

    //Loads settings, returning defaults if the file is missing or unreadable
    public static Settings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json);
                if (loaded is not null)
                {
                    loaded.Normalise();
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to defaults... a corrupt file must never stop the app starting!
        }
        return new Settings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    private void Normalise()
    {
        if (ExportScale is not (1 or 2 or 4 or 8))
        {
            ExportScale = 1;
        }
        ThumbnailSize = Math.Clamp(ThumbnailSize, 120, 480);
        if (string.IsNullOrWhiteSpace(OutputRoot))
        {
            OutputRoot = SessionStore.DefaultRoot;
        }
        if (string.IsNullOrWhiteSpace(FileNameTemplate))
        {
            FileNameTemplate = FileNamer.DefaultTemplate;
        }
        Protocol ??= new ProtocolSettings();
    }
}

//Advanced protocol settings - Timeouts are in milliseconds.
public sealed class ProtocolSettings
{
    public byte AssignedAddress { get; set; } = WqvConstants.DefaultAssignedAddr;
    public int HelloTimeoutMs { get; set; } = (int)WqvConstants.HelloTimeout.TotalMilliseconds;
    public int HelloTries { get; set; } = WqvConstants.HelloTries;
    public int ControlTimeoutMs { get; set; } = (int)WqvConstants.ControlTimeout.TotalMilliseconds;
    public int ControlTries { get; set; } = WqvConstants.ControlTries;
    public int DataTimeoutMs { get; set; } = (int)WqvConstants.DataTimeout.TotalMilliseconds;
    public int DataTries { get; set; } = WqvConstants.DataTries;
    public int DisconnectTries { get; set; } = WqvConstants.DisconnectTries;

    public SessionOptions ToSessionOptions() => new()
    {
        AssignedAddress = AssignedAddress,
        HelloTimeout = TimeSpan.FromMilliseconds(HelloTimeoutMs),
        HelloTries = Math.Max(1, HelloTries),
        ControlTimeout = TimeSpan.FromMilliseconds(ControlTimeoutMs),
        ControlTries = Math.Max(1, ControlTries),
        DataTimeout = TimeSpan.FromMilliseconds(DataTimeoutMs),
        DataTries = Math.Max(1, DataTries),
        DisconnectTries = Math.Max(1, DisconnectTries),
    };
}
