namespace WqvLink.Core.Protocol;

/// <summary>Timeouts, retries and addressing for a session</summary>
public sealed record SessionOptions
{
    public byte AssignedAddress { get; init; } = WqvConstants.DefaultAssignedAddr;
    public TimeSpan HelloTimeout { get; init; } = WqvConstants.HelloTimeout;
    public int HelloTries { get; init; } = WqvConstants.HelloTries;
    public TimeSpan ControlTimeout { get; init; } = WqvConstants.ControlTimeout;
    public int ControlTries { get; init; } = WqvConstants.ControlTries;
    public TimeSpan DataTimeout { get; init; } = WqvConstants.DataTimeout;
    public int DataTries { get; init; } = WqvConstants.DataTries;
    public int DisconnectTries { get; init; } = WqvConstants.DisconnectTries;
    public TimeSpan CancelDisconnectTimeout { get; init; } = TimeSpan.FromMilliseconds(300);
    public int? ExpectedCount { get; init; }
    public static SessionOptions Default { get; } = new();
}

//Result of a successful handshake
public sealed record WatchInfo(byte[] Clock, byte Address)
{
    public string ClockText => Clock.Length >= 3 && Clock[0] < 24 && Clock[1] < 60 && Clock[2] < 60
        ? $"{Clock[0]:00}:{Clock[1]:00}:{Clock[2]:00}"
        : Hex.Format(Clock);
}

public sealed record DownloadResult(byte[] Raw, byte[] Extra, int ImageCount, int RecordSize);

public sealed record DownloadProgress(long BytesReceived, long BytesTotal, int Packets, double BytesPerSecond, TimeSpan? Eta, int ImageCount)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Min(1.0, BytesReceived / (double)BytesTotal);
}

public enum SessionStage
{
    Hello,
    Connect,
    Header,
    Data,
    Disconnect,
}

public sealed class WqvLinkException(SessionStage stage, string message, int checksumErrors = 0) : Exception(message)
{
    public SessionStage Stage { get; } = stage;

    public int ChecksumErrors { get; } = checksumErrors;
}
