using CommunityToolkit.Mvvm.ComponentModel;
using WqvLink.App.Services;
using WqvLink.Core.Diagnostics;
using WqvLink.Core.Logging;
using WqvLink.Core.Protocol;
using WqvLink.Core.Storage;
using WqvLink.Core.Transport;

namespace WqvLink.App.ViewModels;

/// <summary>Drives one download wait for the watch, download, save.</summary>
public sealed partial class DownloadViewModel(Settings settings, string port) : ObservableObject
{
    private const string Instructions =
        "On the watch: IR mode → COM → PC.\nHold it 5–10 cm from the Click, pointing at it, and keep it still.";

    //shown before anything is sent, so the watch is ready when probing starts
    public const string ReadySteps =
        "1. Press MODE on the watch until the display shows IR.\n" +
        "2. Select COM, then PC. The watch now waits for the computer.\n" +
        "3. Hold the watch 5–10 cm from the Click, with the watch's IR window facing the Click's " +
        "IR window. Resting both on a table helps.\n" +
        "4. Press Start. Keep the watch still until the download finishes " +
        "(about 10 seconds per photo, so around 5 minutes for 29 photos).\n\n" +
        "Tip: the watch leaves COM mode after about 2 minutes of waiting, so press Start soon after step 2.";

    [ObservableProperty]
    private string _heading = "Get the watch ready";

    [ObservableProperty]
    private string _detail = ReadySteps;

    //Label for the Start button: "Start", then "Try again" after a failure.
    [ObservableProperty]
    private string _startText = "Start";

    [ObservableProperty]
    private bool _canStart = true;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _canShowLog;

    [ObservableProperty]
    private string _buttonText = "Cancel";

    public string? LogPath { get; private set; }

    //Runs the whole download. Returns the session folder on success, otherwise null
    public async Task<string?> RunAsync(CancellationToken ct)
    {
        IsRunning = true;
        CanStart = false;
        CanShowLog = false;
        ButtonText = "Cancel";
        Progress = 0;
        IsIndeterminate = true;
        var folder = new SessionStore(settings.OutputRoot).CreateSessionFolder(DateTime.Now);
        LogPath = Path.Combine(folder, SessionStore.LogFileName);
        var saved = false;
        var keepFolder = true;
        try
        {
            using var log = new ByteTraceLog(LogPath);
            await using var transport = new SerialTransport(port);

            Heading = $"Opening {port}…";
            await transport.OpenAsync(ct);

            Heading = "Waiting for the watch…";
            var session = new WqvSession(transport, log, settings.Protocol.ToSessionOptions());
            var info = await session.ConnectAsync(new Progress<int>(n => Detail = $"{Instructions}\n\nTries: {n}"), ct);

            Heading = $"Connected. Watch clock {info.ClockText}.";
            Detail = "Reading the photo list…";
            var result = await session.DownloadAllAsync(new Progress<DownloadProgress>(OnProgress), ct);

            if (result.ImageCount == 0)
            {
                Heading = "The watch has no photos.";
                Detail = "";
                keepFolder = false;
                return null;
            }

            Heading = $"Saving {result.ImageCount} photo(s)…";
            IsIndeterminate = true;
            await Task.Run(() => SessionStore.SaveDownload(folder, result, settings.ToExportOptions()), CancellationToken.None);
            saved = true;
            Heading = $"Done: {result.ImageCount} photo(s) saved.";
            Detail = folder;
            return folder;
        }
        catch (OperationCanceledException)
        {
            Heading = "Cancelled.";
            Detail = ReadySteps;
            keepFolder = false;
            OfferRetry();
            return null;
        }
        catch (WqvLinkException ex)
        {
            Heading = "Download failed";
            Detail = $"{ex.Message}\n\n{Hints.For(ex)}";
            CanShowLog = true;
            OfferRetry();
            return null;
        }
        catch (Exception ex) when (ex is TransportException or IOException or UnauthorizedAccessException)
        {
            Heading = "Couldn't talk to the Pico";
            Detail = $"{ex.Message}\n\nCheck the USB cable, or open Pico setup.";
            CanShowLog = true;
            OfferRetry();
            return null;
        }
        finally
        {
            IsRunning = false;
            IsIndeterminate = false;
            ButtonText = "Close";
            if (!saved && !keepFolder)
            {
                TryDelete(folder);
            }
        }
    }

    private void OfferRetry()
    {
        StartText = "Try again";
        CanStart = true;
    }

    private void OnProgress(DownloadProgress p)
    {
        IsIndeterminate = false;
        Progress = p.Fraction * 100;
        Heading = $"Downloading {p.ImageCount} photo(s)…";
        var eta = p.Eta is { } e ? $" · {e:m\\:ss} left" : "";
        var photo = Math.Min(p.ImageCount, (int)(p.BytesReceived / WqvConstants.RecordSize) + 1);
        Detail = $"Photo {photo} of {p.ImageCount}\n{p.BytesReceived / 1024.0:0.0} of {p.BytesTotal / 1024.0:0.0} KB · {p.BytesPerSecond / 1024:0.00} KB/s{eta}";
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Log
        }
    }
}
