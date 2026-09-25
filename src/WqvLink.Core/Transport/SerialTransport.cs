using System.IO.Ports;
using System.Threading.Channels;
using WqvLink.Core.Protocol;

namespace WqvLink.Core.Transport;

/// <summary>
/// The Pico bridge over USB CDC. Always 115200 8N1 with DTR and RTS on
/// Never opens at 1200 baud, which would reboot the Pico into its bootloader
/// </summary>
public sealed class SerialTransport(string portName) : ITransport
{
    private readonly Channel<byte[]> _rx = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private SerialPort? _port;
    private Thread? _reader;
    private volatile bool _stopping;
    private byte[] _leftover = [];
    private int _leftoverOffset;

    public string PortName { get; } = portName;

    public async Task OpenAsync(CancellationToken ct)
    {
        var port = new SerialPort(PortName, WqvConstants.BaudRate, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 50,
            WriteTimeout = 2000,
            ReadBufferSize = 64 * 1024,
        };
        try
        {
            // Opening can block for a while on some USB drivers - keep it off the caller's (UI) thread.
            await Task.Run(port.Open, ct).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            throw new TransportException(OperatingSystem.IsLinux()
                ? $"Permission denied opening {PortName}. Add yourself to the dialout group: `sudo usermod -aG dialout $USER`, then log out and back in."
                : $"{PortName} is in use by another program (or access was denied). Close anything else using the port and try again.", ex);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            port.Dispose();
            throw new TransportException($"Could not open {PortName}: {ex.Message}", ex);
        }

        _port = port;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"SerialTransport {PortName}" };
        _reader.Start();

        // Let the CDC link settle, then drop anything stale
        await Task.Delay(100, ct).ConfigureAwait(false);
        DiscardInput();
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var port = _port ?? throw new InvalidOperationException("Transport is not open");
        ct.ThrowIfCancellationRequested();
        var bytes = data.ToArray();
        return Task.Run(() =>
        {
            try
            {
                port.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
            {
                throw new TransportException($"Write to {PortName} failed: {ex.Message}", ex);
            }
        }, ct);
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, TimeSpan timeout, CancellationToken ct)
    {
        if (_leftoverOffset >= _leftover.Length)
        {
            if (!_rx.Reader.TryRead(out var chunk))
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);
                bool open;
                try
                {
                    open = await _rx.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    return 0; // plain timeout
                }
                catch (TransportException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The reader thread failed. Report it 
                    throw new TransportException($"{PortName} stopped responding: {ex.Message}", ex);
                }
                if (!open)
                {
                    throw new TransportException($"{PortName} was closed or unplugged.");
                }
                if (!_rx.Reader.TryRead(out chunk))
                {
                    return 0;
                }
            }
            _leftover = chunk;
            _leftoverOffset = 0;
        }

        var n = Math.Min(buffer.Length, _leftover.Length - _leftoverOffset);
        _leftover.AsMemory(_leftoverOffset, n).CopyTo(buffer);
        _leftoverOffset += n;
        return n;
    }

    // Drops everything received so far
    public void DiscardInput()
    {
        _leftover = [];
        _leftoverOffset = 0;
        while (_rx.Reader.TryRead(out _))
        {
        }
    }

    private void ReadLoop()
    {
        var port = _port!;
        var buffer = new byte[4096];
        while (!_stopping)
        {
            try
            {
                var n = port.Read(buffer, 0, buffer.Length);
                if (n > 0)
                {
                    _rx.Writer.TryWrite(buffer.AsSpan(0, n).ToArray());
                }
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException) when (!_stopping)
            {
                //a read was aborted if the port is still fine then keep reading
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
            {
                _rx.Writer.TryComplete(_stopping ? null : new TransportException($"{PortName} stopped responding: {ex.Message}", ex));
                return;
            }
        }
        _rx.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        var port = _port;
        var reader = _reader;
        _port = null;
        //closing waits for the reader thread; never do that on the caller's (UI) thread
        await Task.Run(() =>
        {
            try
            {
                port?.Close();
            }
            catch (IOException)
            {
            }
            reader?.Join(500);
            port?.Dispose();
        }).ConfigureAwait(false);
    }
}
