using System.IO.Ports;
using System.Threading.Channels;

namespace SimpleSerialTester.Core;

/// <summary>送受信の出入り口。実際のシリアルポートと、機器なしで試す折り返しを差し替えられるようにする。</summary>
public interface ISerialTransport : IDisposable
{
    bool IsOpen { get; }

    void Open();

    void Close();

    void Write(byte[] data);

    /// <summary>届いた断片と、届いた時刻。画面のスレッドとは別のスレッドから呼ばれる。</summary>
    event Action<byte[], DateTime>? DataReceived;

    /// <summary>受信の途中で起きた異常（ケーブルを抜いた、など）。</summary>
    event Action<string>? Faulted;
}

/// <summary>実際のシリアルポート。</summary>
public sealed class SerialPortTransport(PortSettings settings) : ISerialTransport
{
    private SerialPort? _port;
    private CancellationTokenSource? _cts;

    public event Action<byte[], DateTime>? DataReceived;

    public event Action<string>? Faulted;

    public bool IsOpen => _port is { IsOpen: true };

    /// <summary>このPCにあるポート名を、番号順に返す。</summary>
    public static IReadOnlyList<string> GetPortNames()
    {
        return SerialPort.GetPortNames()
            .Distinct()
            .OrderBy(name => name.Length)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        var port = new SerialPort(settings.PortName, settings.BaudRate, ToParity(settings.Parity), settings.DataBits, ToStopBits(settings.StopBits))
        {
            WriteTimeout = 2000,
        };
        port.Open();

        _port = port;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => ReadLoopAsync(port, token), token);
    }

    public void Close()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        var port = _port;
        _port = null;
        if (port is not null)
        {
            try
            {
                port.Close();
            }
            catch (IOException)
            {
                // 既に抜かれているポートを閉じると失敗することがある。閉じる目的は果たしているので無視する。
            }

            port.Dispose();
        }
    }

    public void Write(byte[] data)
    {
        var port = _port;
        if (port is not { IsOpen: true })
        {
            throw new InvalidOperationException("ポートを開いていません。");
        }

        port.Write(data, 0, data.Length);
    }

    public void Dispose() => Close();

    private async Task ReadLoopAsync(SerialPort port, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var count = await port.BaseStream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count > 0)
                {
                    DataReceived?.Invoke(buffer[..count], DateTime.Now);
                }
            }
        }
        catch (Exception ex)
        {
            // 自分で閉じたときの例外は異常ではない。それ以外（抜けた、など）だけ知らせる。
            if (!token.IsCancellationRequested)
            {
                Faulted?.Invoke(ex.Message);
            }
        }
    }

    private static Parity ToParity(ParityKind parity) => parity switch
    {
        ParityKind.Odd => Parity.Odd,
        ParityKind.Even => Parity.Even,
        ParityKind.Mark => Parity.Mark,
        ParityKind.Space => Parity.Space,
        _ => Parity.None,
    };

    private static StopBits ToStopBits(StopBitsKind stopBits) => stopBits switch
    {
        StopBitsKind.OnePointFive => StopBits.OnePointFive,
        StopBitsKind.Two => StopBits.Two,
        _ => StopBits.One,
    };
}

/// <summary>
/// 機器なしで試すための折り返し。送ったものが、そのまま受信として返ってくる。
/// 実機らしく、1件を2つの断片に分け、少し間をあけて返す。
/// </summary>
public sealed class LoopbackTransport : ISerialTransport
{
    private Channel<byte[]>? _queue;
    private CancellationTokenSource? _cts;

    public event Action<byte[], DateTime>? DataReceived;

#pragma warning disable CS0067 // 折り返しでは異常は起きないが、出入り口の形をそろえるために持つ。
    public event Action<string>? Faulted;
#pragma warning restore CS0067

    /// <summary>送ってから返るまで、また断片と断片の間の待ち時間。</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>1件を2つの断片に分けて返すなら true。</summary>
    public bool Fragment { get; set; } = true;

    public bool IsOpen => _queue is not null;

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        var queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        _queue = queue;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => EchoLoopAsync(queue.Reader, token), token);
    }

    public void Close()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _queue?.Writer.TryComplete();
        _queue = null;
    }

    public void Write(byte[] data)
    {
        var queue = _queue ?? throw new InvalidOperationException("ポートを開いていません。");
        queue.Writer.TryWrite((byte[])data.Clone());
    }

    public void Dispose() => Close();

    private async Task EchoLoopAsync(ChannelReader<byte[]> reader, CancellationToken token)
    {
        try
        {
            await foreach (var data in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                await Task.Delay(Delay, token).ConfigureAwait(false);
                if (Fragment && data.Length >= 2)
                {
                    var half = data.Length / 2;
                    DataReceived?.Invoke(data[..half], DateTime.Now);
                    await Task.Delay(Delay, token).ConfigureAwait(false);
                    DataReceived?.Invoke(data[half..], DateTime.Now);
                }
                else
                {
                    DataReceived?.Invoke(data, DateTime.Now);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 閉じたときに来る。異常ではない。
        }
    }
}
