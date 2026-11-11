namespace SimpleSerialTester.Core;

/// <summary>受信データを区切った1件。</summary>
/// <param name="Data">区切り文字を除いた中身。</param>
/// <param name="Terminated">区切り文字で終わっていれば true。無通信や長さの上限で切った場合は false。</param>
/// <param name="StartedAt">この1件の最初のバイトを受け取った時刻。</param>
public sealed record Frame(byte[] Data, bool Terminated, DateTime StartedAt);

/// <summary>
/// シリアルポートから届く断片を、1件ずつの電文にまとめる。
/// 受信の通知は電文の切れ目で来るとは限らない（1件が2回に分かれて届くことも、2件がまとめて届くこともある）ので、
/// 届いた単位のままログに書かず、区切り文字か無通信の時間で区切り直す。
/// </summary>
public sealed class FrameAssembler
{
    private readonly List<byte> _pending = [];
    private DateTime _startedAt;
    private DateTime _lastReceivedAt;

    /// <summary>区切りに使う改行コード。None のときは無通信の時間だけで区切る。</summary>
    public LineEnding Terminator { get; set; } = LineEnding.CRLF;

    /// <summary>この時間、続きが届かなければ、途中でも1件として確定する。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>区切りが来ないまま、この長さに達したら1件として確定する。</summary>
    public int MaxFrameLength { get; set; } = 4096;

    public bool HasPending => _pending.Count > 0;

    /// <summary>届いた断片を足し、確定した電文を返す。</summary>
    public IReadOnlyList<Frame> Push(ReadOnlySpan<byte> chunk, DateTime now)
    {
        var frames = new List<Frame>();
        var terminator = Terminator.ToBytes();

        foreach (var b in chunk)
        {
            if (_pending.Count == 0)
            {
                _startedAt = now;
            }

            _pending.Add(b);

            if (terminator.Length > 0 && EndsWith(terminator))
            {
                frames.Add(Take(terminated: true, strip: terminator.Length));
            }
            else if (_pending.Count >= MaxFrameLength)
            {
                frames.Add(Take(terminated: false, strip: 0));
            }
        }

        _lastReceivedAt = now;
        return frames;
    }

    /// <summary>無通信の時間を過ぎていれば、残っている途中のデータを1件として返す。</summary>
    public Frame? FlushIfIdle(DateTime now)
    {
        if (_pending.Count == 0 || now - _lastReceivedAt < IdleTimeout)
        {
            return null;
        }

        return Take(terminated: false, strip: 0);
    }

    /// <summary>残っている途中のデータを、時間に関係なく1件として返す（切断時など）。</summary>
    public Frame? FlushAll() => _pending.Count == 0 ? null : Take(terminated: false, strip: 0);

    public void Clear() => _pending.Clear();

    private bool EndsWith(byte[] terminator)
    {
        if (_pending.Count < terminator.Length)
        {
            return false;
        }

        for (var i = 0; i < terminator.Length; i++)
        {
            if (_pending[_pending.Count - terminator.Length + i] != terminator[i])
            {
                return false;
            }
        }

        return true;
    }

    private Frame Take(bool terminated, int strip)
    {
        var data = _pending.GetRange(0, _pending.Count - strip).ToArray();
        _pending.Clear();
        return new Frame(data, terminated, _startedAt);
    }
}
