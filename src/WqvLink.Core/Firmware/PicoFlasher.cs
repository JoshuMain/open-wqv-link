using System.IO.Ports;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Firmware;

public enum FlashStage
{
    EnteringBootloader,
    WaitingForBootsel,
    Copying,
    Rebooting,
    Done,
}

public sealed record FlashProgress(FlashStage Stage, double Percent, string Message);

/// <summary>
/// Puts a Pico into its UF2 bootloader and copies firmware onto the RPI-RP2 drive
/// </summary>
public static class PicoFlasher
{
    public static readonly TimeSpan TouchWait = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ManualWait = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RebootWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Flashes <paramref name="uf2"/> and returns the serial port the Pico reappears on (null if none showed up).
    /// If <paramref name="port"/> is given, a 1200-baud touch is tried first or if that fails,
    /// the user is asked to hold BOOTSEL while plugging in, which hopefully solves the problem lol
    /// </summary>
    public static async Task<string?> FlashAsync(byte[] uf2, string? port, IProgress<FlashProgress>? progress, CancellationToken ct)
    {
        if (Uf2.Validate(uf2) is { } error)
        {
            throw new InvalidDataException($"Not a valid UF2 file: {error}.");
        }

        var drive = FindBootDrives().FirstOrDefault();
        if (drive is null && port is not null)
        {
            progress?.Report(new(FlashStage.EnteringBootloader, 0, $"Asking the Pico on {port} to enter its bootloader…"));
            TouchBootloader(port);
            drive = await WaitForBootDriveAsync(TouchWait, ct).ConfigureAwait(false);
        }
        if (drive is null)
        {
            progress?.Report(new(FlashStage.WaitingForBootsel, 0,
                "Unplug the Pico. Hold the white BOOTSEL button, plug it back in, then release the button."));
            drive = await WaitForBootDriveAsync(ManualWait, ct).ConfigureAwait(false)
                ?? throw new TimeoutException("The Pico's RPI-RP2 drive didn't appear. Try again, holding BOOTSEL while plugging in.");
        }

        if (IsRp2350(drive) && Uf2.FamilyId(uf2) == Uf2.FamilyRp2040)
        {
            throw new InvalidDataException("This board is a Pico 2 (RP2350), but the firmware is for the original Pico (RP2040).");
        }

        progress?.Report(new(FlashStage.Copying, 0, $"Copying firmware to {drive}…"));
        await CopyAsync(uf2, drive, new Progress<double>(p => progress?.Report(new(FlashStage.Copying, p, "Copying firmware…"))), ct).ConfigureAwait(false);

        progress?.Report(new(FlashStage.Rebooting, 100, "Waiting for the Pico to restart…"));
        var newPort = await WaitForPicoPortAsync(RebootWait, ct).ConfigureAwait(false);
        progress?.Report(new(FlashStage.Done, 100, newPort is null ? "Flashed, but no Pico serial port appeared." : $"Flashed. The Pico is on {newPort}."));
        return newPort;
    }

    /// <summary>
    /// The "1200-baud touch": opening the port at 1200 baud and dropping DTR reboots an Arduino-Pico
    /// sketch into the bootloader. This is the ONLY place the app uses 1200 baud
    /// </summary>
    public static void TouchBootloader(string port)
    {
        try
        {
            using var sp = new SerialPort(port, 1200) { DtrEnable = true };
            sp.Open();
            Thread.Sleep(50);
            sp.DtrEnable = false;
            sp.Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // The board often resets while the port is open - the drive watcher decides whether it worked or not
        }
    }

    //True if the bootloader drive belongs to an RP2350 (Pico 2), from INFO_UF2.TXT
    public static bool IsRp2350(string driveRoot)
    {
        try
        {
            return File.ReadAllText(Path.Combine(driveRoot, "INFO_UF2.TXT")).Contains("RP2350", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Mounted drives whose root holds INFO_UF2.TXT (a Pico in bootloader mode).</summary>
    public static IEnumerable<string> FindBootDrives()
    {
        foreach (var root in CandidateRoots())
        {
            bool found;
            try
            {
                found = File.Exists(Path.Combine(root, "INFO_UF2.TXT"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                found = false;
            }
            if (found)
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok;
                try
                {
                    ok = d.DriveType is DriveType.Removable or DriveType.Fixed && d.IsReady;
                }
                catch (IOException)
                {
                    ok = false;
                }
                if (ok)
                {
                    yield return d.RootDirectory.FullName;
                }
            }
            yield break;
        }

        var user = Environment.UserName;
        string[] parents = OperatingSystem.IsMacOS() ? ["/Volumes"] : [$"/media/{user}", $"/run/media/{user}", "/media", "/mnt"];
        foreach (var parent in parents.Where(Directory.Exists))
        {
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(parent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var dir in dirs)
            {
                yield return dir;
            }
        }
    }

    public static async Task<string?> WaitForBootDriveAsync(TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (FindBootDrives().FirstOrDefault() is { } drive)
            {
                return drive;
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return null;
    }

    public static async Task<string?> WaitForPicoPortAsync(TimeSpan timeout, CancellationToken ct)
    {
        var end = DateTime.UtcNow + timeout;
        // Give the drive a moment to vanish so we don't pick up a stale port entry.
        await Task.Delay(1000, ct).ConfigureAwait(false);
        while (DateTime.UtcNow < end)
        {
            if (!FindBootDrives().Any() && PortDiscovery.AutoSelect(PortDiscovery.ListPorts()) is { } pico)
            {
                return pico.Name;
            }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>
    /// Copies the image as firmware.uf2. The drive usually disappears as soon as the last block lands;
    /// an I/O error after 100% is written counts as success.
    /// </summary>
    public static async Task CopyAsync(byte[] uf2, string driveRoot, IProgress<double>? progress, CancellationToken ct)
    {
        var path = Path.Combine(driveRoot, "firmware.uf2");
        var written = 0;
        try
        {
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            const int chunk = 16 * Uf2.BlockSize;
            while (written < uf2.Length)
            {
                ct.ThrowIfCancellationRequested();
                var n = Math.Min(chunk, uf2.Length - written);
                await fs.WriteAsync(uf2.AsMemory(written, n), ct).ConfigureAwait(false);
                written += n;
                progress?.Report(100.0 * written / uf2.Length);
            }
            fs.Flush(true);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or UnauthorizedAccessException && written >= uf2.Length)
        {
            // The Pico rebooted mid-flush: that's success.
        }
    }
}
