using System.Globalization;

namespace SimpleSerialTester.Core;

public enum LogDirection
{
    Sent,
    Received,
    Info,
}

/// <summary>ログの1行。送信・受信はバイト列を持ち、表示の形（文字列かHEXか）は後から選べる。</summary>
/// <param name="Time">送信した時刻、または受信の最初のバイトが届いた時刻。</param>
/// <param name="Data">送受信した中身。情報の行では空。</param>
/// <param name="Unterminated">受信で、区切り文字が来ないまま確定した行なら true。</param>
/// <param name="Note">情報の行の文面。</param>
public sealed record LogEntry(DateTime Time, LogDirection Direction, byte[] Data, bool Unterminated = false, string? Note = null)
{
    public static LogEntry Info(DateTime time, string note) => new(time, LogDirection.Info, [], false, note);
}

public static class LogFormatter
{
    /// <summary>年月日と24時間表記、ミリ秒まで。あとから並べ替えても崩れない形にする。</summary>
    public const string FileTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    public const string ScreenTimeFormat = "HH:mm:ss.fff";

    public static string DirectionLabel(LogDirection direction) => direction switch
    {
        LogDirection.Sent => "送信",
        LogDirection.Received => "受信",
        _ => "情報",
    };

    public static string Body(LogEntry entry, bool hex)
    {
        if (entry.Direction == LogDirection.Info)
        {
            return entry.Note ?? "";
        }

        return hex ? HexCodec.Format(entry.Data) : TextCodec.ToDisplay(entry.Data);
    }

    /// <summary>ファイルへ保存するときの1行。タブ区切りで、時刻・向き・中身・補足の順。</summary>
    public static string FileLine(LogEntry entry, bool hex)
    {
        var time = entry.Time.ToString(FileTimeFormat, CultureInfo.InvariantCulture);
        var note = entry.Unterminated ? "区切りなし" : "";
        return $"{time}\t{DirectionLabel(entry.Direction)}\t{Body(entry, hex)}\t{note}".TrimEnd('\t');
    }
}
