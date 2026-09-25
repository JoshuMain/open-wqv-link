using System.Text.Json;
using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Imaging;

public enum ImageFormat
{
    Png,
    Bmp,
}

//How images are written out
public sealed record ExportOptions
{
    public DecodeOptions Decode { get; init; } = DecodeOptions.Default;
    public ImageFormat Format { get; init; } = ImageFormat.Png;

    public int Scale { get; init; } = 1;

    public string FileNameTemplate { get; init; } = FileNamer.DefaultTemplate;

    public bool SetFileDates { get; init; } = true;

    public bool EmbedMetadata { get; init; } = true;

    public bool Sidecars { get; init; }

    public static ExportOptions Default { get; } = new();

    public string Extension => Format == ImageFormat.Bmp ? ".bmp" : ".png";
}

//Writes images to disk
public static class ImageExporter
{
    private static readonly JsonSerializerOptions SidecarJson = new() { WriteIndented = true };

    public static byte[] Encode(WqvImage img, ExportOptions opts)
    {
        var grey = ImageDecoder.ToGrey8(img, opts.Decode);
        if (opts.Format == ImageFormat.Bmp)
        {
            return BmpWriter.Encode(grey, ImageSize, ImageSize, opts.Scale);
        }
        var meta = opts.EmbedMetadata ? PngMetadata.For(img) : null;
        return PngWriter.Encode(grey, ImageSize, ImageSize, meta, opts.Scale);
    }

    public static string Export(WqvImage img, string folder, ExportOptions? opts = null) =>
        Export(img, folder, opts ?? ExportOptions.Default, usedNames: null);


    /// Exports each image into <paramref name="folder"/> - existing files are overwritten
    public static IReadOnlyList<string> ExportAll(IEnumerable<WqvImage> images, string folder, ExportOptions? opts = null)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return images.Select(img => Export(img, folder, opts ?? ExportOptions.Default, used)).ToList();
    }

    private static string Export(WqvImage img, string folder, ExportOptions opts, HashSet<string>? usedNames)
    {
        Directory.CreateDirectory(folder);
        var baseName = FileNamer.Render(opts.FileNameTemplate, img);
        var name = baseName;
        for (var n = 2; usedNames is not null && !usedNames.Add(name); n++)
        {
            name = $"{baseName}-{n}";
        }
        var path = Path.Combine(folder, name + opts.Extension);

        File.WriteAllBytes(path, Encode(img, opts));
        if (opts.SetFileDates)
        {
            StampFileTimes(path, img.Taken);
        }
        if (opts.Sidecars)
        {
            File.WriteAllText(Path.ChangeExtension(path, ".json"), SidecarFor(img));
        }
        return path;
    }

    public static string SidecarFor(WqvImage img) => JsonSerializer.Serialize(new
    {
        index = img.Index,
        name = img.Name,
        date_bytes = img.DateBytes.Select(b => (int)b).ToArray(),
        stamp = img.Stamp,
    }, SidecarJson);


    public static void StampFileTimes(string path, DateTime? taken)
    {
        if (taken is not { } t)
        {
            return;
        }
        var local = DateTime.SpecifyKind(t, DateTimeKind.Local);
        try
        {
            File.SetCreationTime(path, local);
        }
        catch (PlatformNotSupportedException)
        {
            // Some Unix file systems have no settable creation time.
        }
        File.SetLastWriteTime(path, local);
    }
}
