using System.Collections.Concurrent;
using System.Text;
using SimpleSerialTester.Core;

namespace SimpleSerialTester.Core.Tests;

public class AutoReplyTests
{
    [Fact]
    public void 含まれている決まりだけが登録順に当たる()
    {
        var rules = new[]
        {
            new AutoReplyRule { Contains = "NG", Reply = "RETRY" },
            new AutoReplyRule { Contains = "ERR", Reply = "RESET" },
            new AutoReplyRule { Contains = "OK", Reply = "NEXT" },
        };

        var matched = AutoReplyEngine.Match(rules, "ERR: NG 3").Select(rule => rule.Reply).ToArray();

        Assert.Equal(["RETRY", "RESET"], matched);
    }

    [Fact]
    public void 無効な決まりと空の決まりは当たらない()
    {
        var rules = new[]
        {
            new AutoReplyRule { Enabled = false, Contains = "NG", Reply = "RETRY" },
            new AutoReplyRule { Contains = "", Reply = "X" },
            new AutoReplyRule { Contains = "NG", Reply = "" },
        };

        Assert.Empty(AutoReplyEngine.Match(rules, "NG"));
    }

    [Fact]
    public void 大文字と小文字は区別する()
    {
        var rules = new[] { new AutoReplyRule { Contains = "NG", Reply = "RETRY" } };

        Assert.Empty(AutoReplyEngine.Match(rules, "ng"));
    }

    [Fact]
    public void 一秒あたりの回数を超えたら止める()
    {
        var limiter = new ReplyLimiter(maxPerSecond: 3);
        var t0 = new DateTime(2026, 11, 11, 10, 0, 0);

        Assert.True(limiter.TryAcquire(t0));
        Assert.True(limiter.TryAcquire(t0.AddMilliseconds(100)));
        Assert.True(limiter.TryAcquire(t0.AddMilliseconds(200)));
        Assert.False(limiter.TryAcquire(t0.AddMilliseconds(300)));
        Assert.True(limiter.TryAcquire(t0.AddMilliseconds(1000)));
    }
}

public class SendHistoryTests
{
    [Fact]
    public void 新しいものが先頭に来る()
    {
        var history = new List<SendItem>();

        SendHistory.Push(history, "A", isHex: false);
        SendHistory.Push(history, "B", isHex: false);

        Assert.Equal(["B", "A"], history.Select(item => item.Data));
    }

    [Fact]
    public void 同じ内容は一件にまとめて先頭へ移す()
    {
        var history = new List<SendItem>();

        SendHistory.Push(history, "A", isHex: false);
        SendHistory.Push(history, "B", isHex: false);
        SendHistory.Push(history, "A", isHex: false);

        Assert.Equal(["A", "B"], history.Select(item => item.Data));
    }

    [Fact]
    public void 文字列とHEXは別のものとして残す()
    {
        var history = new List<SendItem>();

        SendHistory.Push(history, "41", isHex: false);
        SendHistory.Push(history, "41", isHex: true);

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void 上限を超えた古いものは捨てる()
    {
        var history = new List<SendItem>();

        for (var i = 0; i < 5; i++)
        {
            SendHistory.Push(history, i.ToString(), isHex: false, limit: 3);
        }

        Assert.Equal(["4", "3", "2"], history.Select(item => item.Data));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void 保存したものを読み直すと元に戻る()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sst-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var settings = new AppSettings
            {
                PortName = "COM7",
                BaudRate = 115200,
                Parity = ParityKind.Even,
                StopBits = StopBitsKind.Two,
                SendLineEnding = LineEnding.LF,
                Favorites = [new SendItem { Name = "バージョン確認", Data = "VER?", IsHex = false }],
                AutoReplies = [new AutoReplyRule { Contains = "NG", Reply = "02 15 03", ReplyIsHex = true }],
            };
            SendHistory.Push(settings.History, "AT", isHex: false);

            SettingsStore.Save(path, settings);
            var loaded = SettingsStore.Load(path);

            Assert.Equal("COM7", loaded.PortName);
            Assert.Equal(115200, loaded.BaudRate);
            Assert.Equal(ParityKind.Even, loaded.Parity);
            Assert.Equal(StopBitsKind.Two, loaded.StopBits);
            Assert.Equal(LineEnding.LF, loaded.SendLineEnding);
            Assert.Equal("バージョン確認", Assert.Single(loaded.Favorites).Name);
            Assert.True(Assert.Single(loaded.AutoReplies).ReplyIsHex);
            Assert.Equal("AT", Assert.Single(loaded.History).Data);
            Assert.Contains("バージョン確認", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ファイルが無いときと壊れているときは既定値で始める()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Equal(9600, SettingsStore.Load(Path.Combine(directory, "missing.json")).BaudRate);

            var broken = Path.Combine(directory, "broken.json");
            File.WriteAllText(broken, "{ this is not json");
            Assert.Equal(9600, SettingsStore.Load(broken).BaudRate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public class LogFormatterTests
{
    private static readonly DateTime Time = new(2026, 11, 11, 15, 4, 5, 678);

    [Fact]
    public void ファイルの時刻は年月日と24時間表記とミリ秒まで書く()
    {
        var line = LogFormatter.FileLine(new LogEntry(Time, LogDirection.Sent, "VER?\r\n"u8.ToArray()), hex: false);

        Assert.Equal("2026-11-11 15:04:05.678\t送信\tVER?\\r\\n", line);
    }

    [Fact]
    public void HEX表示と区切りなしの補足を書ける()
    {
        var line = LogFormatter.FileLine(new LogEntry(Time, LogDirection.Received, [0x02, 0x41], Unterminated: true), hex: true);

        Assert.Equal("2026-11-11 15:04:05.678\t受信\t02 41\t区切りなし", line);
    }

    [Fact]
    public void 情報の行は文面を書く()
    {
        var line = LogFormatter.FileLine(LogEntry.Info(Time, "接続しました"), hex: true);

        Assert.Equal("2026-11-11 15:04:05.678\t情報\t接続しました", line);
    }
}

public class PortSettingsTests
{
    [Fact]
    public void 要約は通信条件を短く書く()
    {
        Assert.Equal("9600 8N1", new PortSettings("COM1", 9600, 8, ParityKind.None, StopBitsKind.One).Summary);
        Assert.Equal("115200 7E2", new PortSettings("COM1", 115200, 7, ParityKind.Even, StopBitsKind.Two).Summary);
    }
}

public class LoopbackTransportTests
{
    [Fact]
    public async Task 送ったものが断片に分かれて順番どおりに返る()
    {
        using var transport = new LoopbackTransport { Delay = TimeSpan.FromMilliseconds(1) };
        var chunks = new ConcurrentQueue<byte[]>();
        transport.DataReceived += (data, _) => chunks.Enqueue(data);
        transport.Open();

        transport.Write(Encoding.ASCII.GetBytes("HELLO\r\n"));
        transport.Write(Encoding.ASCII.GetBytes("WORLD\r\n"));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (chunks.Sum(chunk => chunk.Length) < 14 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.Equal(4, chunks.Count);
        Assert.Equal("HELLO\r\nWORLD\r\n", Encoding.ASCII.GetString(chunks.SelectMany(chunk => chunk).ToArray()));
    }

    [Fact]
    public async Task 折り返しと組み立てをつなぐと電文が元に戻る()
    {
        using var transport = new LoopbackTransport { Delay = TimeSpan.FromMilliseconds(1) };
        var assembler = new FrameAssembler { Terminator = LineEnding.CRLF };
        var frames = new ConcurrentQueue<Frame>();
        var gate = new object();
        transport.DataReceived += (data, time) =>
        {
            lock (gate)
            {
                foreach (var frame in assembler.Push(data, time))
                {
                    frames.Enqueue(frame);
                }
            }
        };
        transport.Open();

        transport.Write(Encoding.ASCII.GetBytes("TEMP=25.0\r\n"));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (frames.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Assert.Equal("TEMP=25.0", Encoding.ASCII.GetString(Assert.Single(frames).Data));
    }

    [Fact]
    public void 開いていないときに送ると失敗する()
    {
        using var transport = new LoopbackTransport();

        Assert.Throws<InvalidOperationException>(() => transport.Write([0x00]));
    }
}
