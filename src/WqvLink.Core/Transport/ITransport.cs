namespace WqvLink.Core.Transport;

//A byte pipe to the IR bridge -- or a simulation of one
public interface ITransport : IAsyncDisposable
{
    Task OpenAsync(CancellationToken ct);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    //Waits up to <paramref name="timeout"/> for at least one byte. Returns 0 on timeout.
    Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct);

    void DiscardInput();
}

//A transport could not be opened or failed while in use. The message is user-facing
public sealed class TransportException(string message, Exception? inner = null) : Exception(message, inner);

public static class TransportExtensions
{
    //Collects everything that arrives during the duration
    public static async Task<byte[]> ReadForAsync(this ITransport t, TimeSpan duration, CancellationToken ct)
    {
        var output = new List<byte>();
        var buffer = new byte[4096];
        var deadline = DateTime.UtcNow + duration;
        while (true)
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                return output.ToArray();
            }
            var n = await t.ReadAsync(buffer, left, ct).ConfigureAwait(false);
            output.AddRange(buffer.AsSpan(0, n));
        }
    }
}
