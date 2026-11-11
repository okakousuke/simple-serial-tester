using System.Globalization;
using SimpleSerialTester.Core;

namespace SimpleSerialTester.ViewModels;

/// <summary>ログの1行。中身はバイト列のまま持ち、文字列かHEXかは表示のときに決める。</summary>
public sealed class LogEntryViewModel(LogEntry entry, bool hex) : ObservableObject
{
    private bool _hex = hex;

    public LogEntry Model { get; } = entry;

    public LogDirection Direction => Model.Direction;

    public string Time { get; } = entry.Time.ToString(LogFormatter.ScreenTimeFormat, CultureInfo.InvariantCulture);

    public string DirectionLabel { get; } = LogFormatter.DirectionLabel(entry.Direction);

    public string Body => LogFormatter.Body(Model, _hex);

    /// <summary>中身のあとに薄く添える補足。</summary>
    public string Suffix
    {
        get
        {
            if (Model.Direction == LogDirection.Info)
            {
                return "";
            }

            if (Model.Data.Length == 0)
            {
                return "（空）";
            }

            return Model.Unterminated ? "  （区切りなし）" : "";
        }
    }

    public void SetHex(bool hex)
    {
        if (_hex != hex)
        {
            _hex = hex;
            Raise(nameof(Body));
        }
    }
}

/// <summary>登録リストの1件。名前と中身をあとから書き換えられる。</summary>
public sealed class SendItemViewModel : ObservableObject
{
    private string _name = "";
    private string _data = "";
    private bool _isHex;

    public SendItemViewModel()
    {
    }

    public SendItemViewModel(SendItem item)
    {
        _name = item.Name;
        _data = item.Data;
        _isHex = item.IsHex;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                Raise(nameof(DisplayName));
            }
        }
    }

    public string Data
    {
        get => _data;
        set
        {
            if (Set(ref _data, value))
            {
                Raise(nameof(DisplayName));
            }
        }
    }

    public bool IsHex
    {
        get => _isHex;
        set => Set(ref _isHex, value);
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Data : Name;

    public SendItem ToModel() => new() { Name = Name, Data = Data, IsHex = IsHex };
}

/// <summary>自動応答の決まり1件。</summary>
public sealed class AutoReplyRuleViewModel : ObservableObject
{
    private bool _enabled = true;
    private string _contains = "";
    private string _reply = "";
    private bool _replyIsHex;

    public AutoReplyRuleViewModel()
    {
    }

    public AutoReplyRuleViewModel(AutoReplyRule rule)
    {
        _enabled = rule.Enabled;
        _contains = rule.Contains;
        _reply = rule.Reply;
        _replyIsHex = rule.ReplyIsHex;
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public string Contains
    {
        get => _contains;
        set => Set(ref _contains, value);
    }

    public string Reply
    {
        get => _reply;
        set => Set(ref _reply, value);
    }

    public bool ReplyIsHex
    {
        get => _replyIsHex;
        set => Set(ref _replyIsHex, value);
    }

    public AutoReplyRule ToModel() => new() { Enabled = Enabled, Contains = Contains, Reply = Reply, ReplyIsHex = ReplyIsHex };
}
