using System.Globalization;
using System.Text;

namespace SimpleSerialTester.Core;

/// <summary>「01 02 0A」のような16進表記と、バイト列を相互に変換する。</summary>
public static class HexCodec
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ',', '-', ':'];

    /// <summary>
    /// 16進表記をバイト列にする。区切りは空白・カンマ・ハイフン・コロン。
    /// 「0x」の接頭辞と、区切りなしの連続表記（01020A）も受け付ける。
    /// </summary>
    public static bool TryParse(string? text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "送信するデータがありません。";
            return false;
        }

        var result = new List<byte>();
        foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
            if (token.Length == 0)
            {
                error = $"16進数として読めません: {raw}";
                return false;
            }

            // 1桁だけの「A」は 0x0A として扱う。3桁以上の奇数桁は、どこで切るか決められないので誤りにする。
            if (token.Length == 1)
            {
                token = "0" + token;
            }
            else if (token.Length % 2 != 0)
            {
                error = $"桁数が奇数です: {raw}";
                return false;
            }

            for (var i = 0; i < token.Length; i += 2)
            {
                if (!byte.TryParse(token.AsSpan(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    error = $"16進数として読めません: {raw}";
                    return false;
                }

                result.Add(value);
            }
        }

        bytes = result.ToArray();
        return true;
    }

    /// <summary>バイト列を「01 02 0A」の形にする。必ず2桁、送受信の順のまま並べる。</summary>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return "";
        }

        var sb = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
