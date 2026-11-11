using System.Globalization;
using System.Text;

namespace SimpleSerialTester.Core;

/// <summary>文字列とバイト列の変換。ログに出すときは、制御文字を見える形にする。</summary>
public static class TextCodec
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] Encode(string text) => Utf8.GetBytes(text);

    /// <summary>
    /// ログ表示用の文字列にする。改行やタブは \r \n \t、そのほかの制御文字は \xNN と書く。
    /// UTF-8の文字として読めないバイトは \x?? と書く（中身はHEX表示で確かめる）。
    /// </summary>
    public static string ToDisplay(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        var text = Utf8.GetString(bytes);

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '�': sb.Append("\\x??"); break;
                default:
                    if (char.IsControl(ch))
                    {
                        sb.Append("\\x").Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
