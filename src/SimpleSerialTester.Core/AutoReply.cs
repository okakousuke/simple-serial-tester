namespace SimpleSerialTester.Core;

/// <summary>「受信にこの文字列が含まれていたら、これを送る」という1件の決まり。</summary>
public sealed class AutoReplyRule
{
    public bool Enabled { get; set; } = true;

    /// <summary>受信の文字列表示に、この文字列が含まれていたら反応する。</summary>
    public string Contains { get; set; } = "";

    /// <summary>送る内容。</summary>
    public string Reply { get; set; } = "";

    /// <summary>送る内容をHEXとして解釈するなら true。</summary>
    public bool ReplyIsHex { get; set; }
}

public static class AutoReplyEngine
{
    /// <summary>受信した1件に当てはまる決まりを、登録順に返す。</summary>
    public static IEnumerable<AutoReplyRule> Match(IEnumerable<AutoReplyRule> rules, string receivedText)
    {
        foreach (var rule in rules)
        {
            if (rule.Enabled && rule.Contains.Length > 0 && rule.Reply.Length > 0
                && receivedText.Contains(rule.Contains, StringComparison.Ordinal))
            {
                yield return rule;
            }
        }
    }
}

/// <summary>
/// 自動応答が止まらなくなるのを防ぐための歯止め。
/// 返した内容に相手がまた同じ応答を返すと、送受信が延々と続く。1秒あたりの回数に上限を置く。
/// </summary>
public sealed class ReplyLimiter(int maxPerSecond = 20)
{
    private readonly Queue<DateTime> _recent = new();

    public bool TryAcquire(DateTime now)
    {
        while (_recent.Count > 0 && now - _recent.Peek() >= TimeSpan.FromSeconds(1))
        {
            _recent.Dequeue();
        }

        if (_recent.Count >= maxPerSecond)
        {
            return false;
        }

        _recent.Enqueue(now);
        return true;
    }

    public void Reset() => _recent.Clear();
}
