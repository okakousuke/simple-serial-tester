namespace SimpleSerialTester.Core;

/// <summary>送信の末尾に付ける、または受信を区切るための改行コード。</summary>
public enum LineEnding
{
    None,
    CR,
    LF,
    CRLF,
}

public static class LineEndingExtensions
{
    public static byte[] ToBytes(this LineEnding ending) => ending switch
    {
        LineEnding.CR => [0x0D],
        LineEnding.LF => [0x0A],
        LineEnding.CRLF => [0x0D, 0x0A],
        _ => [],
    };
}
