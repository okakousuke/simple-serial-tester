using SimpleSerialTester.Core;

namespace SimpleSerialTester.Core.Tests;

public class HexCodecTests
{
    [Theory]
    [InlineData("01 02 0A", new byte[] { 0x01, 0x02, 0x0A })]
    [InlineData("01,02,0a", new byte[] { 0x01, 0x02, 0x0A })]
    [InlineData("0x01 0x02 0x0A", new byte[] { 0x01, 0x02, 0x0A })]
    [InlineData("01020A", new byte[] { 0x01, 0x02, 0x0A })]
    [InlineData("1 2 A", new byte[] { 0x01, 0x02, 0x0A })]
    [InlineData("AA-BB:CC", new byte[] { 0xAA, 0xBB, 0xCC })]
    [InlineData("  ff\t00\r\n", new byte[] { 0xFF, 0x00 })]
    public void 区切りや接頭辞が違っても同じバイト列になる(string text, byte[] expected)
    {
        Assert.True(HexCodec.TryParse(text, out var bytes, out var error), error);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0G")]
    [InlineData("123")]
    [InlineData("0x")]
    [InlineData("hello")]
    public void 読めない入力は理由を付けて失敗する(string text)
    {
        Assert.False(HexCodec.TryParse(text, out var bytes, out var error));
        Assert.Empty(bytes);
        Assert.NotEqual("", error);
    }

    [Fact]
    public void 表示は必ず2桁で送受信の順のまま並ぶ()
    {
        Assert.Equal("01 0A FF 00", HexCodec.Format([0x01, 0x0A, 0xFF, 0x00]));
        Assert.Equal("", HexCodec.Format([]));
    }

    [Fact]
    public void 表示したものを読み直すと元に戻る()
    {
        byte[] original = [0x00, 0x7F, 0x80, 0xFF, 0x0D, 0x0A];
        Assert.True(HexCodec.TryParse(HexCodec.Format(original), out var bytes, out _));
        Assert.Equal(original, bytes);
    }
}

public class TextCodecTests
{
    [Fact]
    public void 制御文字は見える形で表示する()
    {
        Assert.Equal("OK\\r\\n", TextCodec.ToDisplay("OK\r\n"u8));
        Assert.Equal("a\\tb", TextCodec.ToDisplay("a\tb"u8));
        Assert.Equal("\\x02DATA\\x03", TextCodec.ToDisplay([0x02, 0x44, 0x41, 0x54, 0x41, 0x03]));
    }

    [Fact]
    public void 日本語はそのまま表示する()
    {
        Assert.Equal("温度=25", TextCodec.ToDisplay(TextCodec.Encode("温度=25")));
    }

    [Fact]
    public void 文字として読めないバイトは目印を出す()
    {
        Assert.Contains("\\x??", TextCodec.ToDisplay([0x41, 0xFF, 0x42]));
    }
}

public class PayloadBuilderTests
{
    [Theory]
    [InlineData(LineEnding.None, new byte[] { 0x41, 0x54 })]
    [InlineData(LineEnding.CR, new byte[] { 0x41, 0x54, 0x0D })]
    [InlineData(LineEnding.LF, new byte[] { 0x41, 0x54, 0x0A })]
    [InlineData(LineEnding.CRLF, new byte[] { 0x41, 0x54, 0x0D, 0x0A })]
    public void 文字列には改行コードを足す(LineEnding ending, byte[] expected)
    {
        Assert.True(PayloadBuilder.TryBuild("AT", isHex: false, ending, out var payload, out var error), error);
        Assert.Equal(expected, payload);
    }

    [Fact]
    public void HEXには改行コードを足さない()
    {
        Assert.True(PayloadBuilder.TryBuild("02 41 03", isHex: true, LineEnding.CRLF, out var payload, out _));
        Assert.Equal(new byte[] { 0x02, 0x41, 0x03 }, payload);
    }

    [Fact]
    public void 空の入力は送らない()
    {
        Assert.False(PayloadBuilder.TryBuild("", isHex: false, LineEnding.CRLF, out _, out var textError));
        Assert.False(PayloadBuilder.TryBuild("", isHex: true, LineEnding.CRLF, out _, out var hexError));
        Assert.NotEqual("", textError);
        Assert.NotEqual("", hexError);
    }
}
