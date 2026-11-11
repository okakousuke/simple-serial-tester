using System.Text;
using SimpleSerialTester.Core;

namespace SimpleSerialTester.Core.Tests;

public class FrameAssemblerTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 10, 0, 0);

    private static byte[] B(string text) => Encoding.ASCII.GetBytes(text);

    private static string S(Frame frame) => Encoding.ASCII.GetString(frame.Data);

    [Fact]
    public void 一件が二回に分かれて届いても一件にまとまる()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF };

        Assert.Empty(assembler.Push(B("TEMP=2"), T0));
        var frames = assembler.Push(B("5.0\r\n"), T0.AddMilliseconds(5));

        var frame = Assert.Single(frames);
        Assert.Equal("TEMP=25.0", S(frame));
        Assert.True(frame.Terminated);
        Assert.Equal(T0, frame.StartedAt);
    }

    [Fact]
    public void 二件がまとめて届いたら二件に分かれる()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF };

        var frames = assembler.Push(B("OK\r\nERR 3\r\n"), T0);

        Assert.Equal(2, frames.Count);
        Assert.Equal("OK", S(frames[0]));
        Assert.Equal("ERR 3", S(frames[1]));
    }

    [Fact]
    public void 区切り文字そのものが二回に分かれて届いても区切れる()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF };

        Assert.Empty(assembler.Push(B("OK\r"), T0));
        var frame = Assert.Single(assembler.Push(B("\nNEXT"), T0.AddMilliseconds(1)));

        Assert.Equal("OK", S(frame));
        Assert.True(assembler.HasPending);
    }

    [Fact]
    public void 区切りがLFのときCRは中身に残る()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.LF };

        var frame = Assert.Single(assembler.Push(B("OK\r\n"), T0));

        Assert.Equal("OK\r", S(frame));
    }

    [Fact]
    public void 続きが来なければ無通信の時間で確定する()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF, IdleTimeout = TimeSpan.FromMilliseconds(50) };
        assembler.Push(B("PARTIAL"), T0);

        Assert.Null(assembler.FlushIfIdle(T0.AddMilliseconds(49)));
        var frame = assembler.FlushIfIdle(T0.AddMilliseconds(50));

        Assert.NotNull(frame);
        Assert.Equal("PARTIAL", S(frame));
        Assert.False(frame.Terminated);
        Assert.False(assembler.HasPending);
    }

    [Fact]
    public void 区切りなしの設定では無通信の時間だけで区切る()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.None, IdleTimeout = TimeSpan.FromMilliseconds(20) };

        Assert.Empty(assembler.Push([0x02, 0x10], T0));
        Assert.Empty(assembler.Push([0x20, 0x03], T0.AddMilliseconds(5)));
        var frame = assembler.FlushIfIdle(T0.AddMilliseconds(25));

        Assert.NotNull(frame);
        Assert.Equal(new byte[] { 0x02, 0x10, 0x20, 0x03 }, frame.Data);
        Assert.Equal(T0, frame.StartedAt);
    }

    [Fact]
    public void 区切りが来ないまま長くなったら上限で切る()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF, MaxFrameLength = 4 };

        var frames = assembler.Push(B("ABCDEFGH"), T0);

        Assert.Equal(2, frames.Count);
        Assert.Equal("ABCD", S(frames[0]));
        Assert.False(frames[0].Terminated);
    }

    [Fact]
    public void 切断時は残りをすべて確定する()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF };
        assembler.Push(B("TAIL"), T0);

        var frame = assembler.FlushAll();

        Assert.NotNull(frame);
        Assert.Equal("TAIL", S(frame));
        Assert.Null(assembler.FlushAll());
    }

    [Fact]
    public void 空の行も一件として数える()
    {
        var assembler = new FrameAssembler { Terminator = LineEnding.LF };

        var frames = assembler.Push(B("\n\nA\n"), T0);

        Assert.Equal(3, frames.Count);
        Assert.Empty(frames[0].Data);
        Assert.Equal("A", S(frames[2]));
    }
}
