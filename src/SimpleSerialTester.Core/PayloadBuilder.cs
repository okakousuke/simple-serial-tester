namespace SimpleSerialTester.Core;

/// <summary>入力欄の内容から、実際に送るバイト列を作る。</summary>
public static class PayloadBuilder
{
    /// <summary>
    /// 文字列のときは UTF-8 にして改行コードを足す。
    /// HEX のときは書かれたバイトをそのまま送り、改行コードは足さない（必要なら 0D 0A と書く）。
    /// </summary>
    public static bool TryBuild(string? input, bool isHex, LineEnding ending, out byte[] payload, out string error)
    {
        payload = [];
        error = "";

        if (isHex)
        {
            return HexCodec.TryParse(input, out payload, out error);
        }

        if (string.IsNullOrEmpty(input))
        {
            error = "送信するデータがありません。";
            return false;
        }

        var body = TextCodec.Encode(input);
        var tail = ending.ToBytes();
        payload = new byte[body.Length + tail.Length];
        body.CopyTo(payload, 0);
        tail.CopyTo(payload, body.Length);
        return true;
    }
}
