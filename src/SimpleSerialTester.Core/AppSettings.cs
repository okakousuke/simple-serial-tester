using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleSerialTester.Core;

/// <summary>送信データの1件。履歴と登録リストの両方で使う。</summary>
public sealed class SendItem
{
    /// <summary>登録リストでの呼び名。履歴では空。</summary>
    public string Name { get; set; } = "";

    public string Data { get; set; } = "";

    public bool IsHex { get; set; }
}

/// <summary>終了時に保存し、次の起動で戻す内容。</summary>
public sealed class AppSettings
{
    public string PortName { get; set; } = "";

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public ParityKind Parity { get; set; } = ParityKind.None;

    public StopBitsKind StopBits { get; set; } = StopBitsKind.One;

    public LineEnding SendLineEnding { get; set; } = LineEnding.CRLF;

    public LineEnding ReceiveLineEnding { get; set; } = LineEnding.CRLF;

    public int IdleTimeoutMs { get; set; } = 50;

    public bool SendAsHex { get; set; }

    public bool ShowHex { get; set; }

    public bool ClearInputAfterSend { get; set; }

    public bool AutoScroll { get; set; } = true;

    public int RepeatIntervalMs { get; set; } = 1000;

    public int SequenceIntervalMs { get; set; } = 500;

    public bool AutoReplyEnabled { get; set; }

    public List<SendItem> History { get; set; } = [];

    public List<SendItem> Favorites { get; set; } = [];

    public List<AutoReplyRule> AutoReplies { get; set; } = [];
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>設定を読む。ファイルが無い、または壊れているときは既定値を返す。</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}

/// <summary>送信の履歴。新しいものを先頭に置き、同じ内容は1件にまとめる。</summary>
public static class SendHistory
{
    public const int DefaultLimit = 200;

    /// <summary>先頭へ足す。同じ内容（データとHEXの別が同じ）が既にあれば、それを先頭へ移す。</summary>
    public static void Push(IList<SendItem> history, string data, bool isHex, int limit = DefaultLimit)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].IsHex == isHex && history[i].Data == data)
            {
                history.RemoveAt(i);
            }
        }

        history.Insert(0, new SendItem { Data = data, IsHex = isHex });

        while (history.Count > limit)
        {
            history.RemoveAt(history.Count - 1);
        }
    }
}
