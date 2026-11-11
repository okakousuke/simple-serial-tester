using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using SimpleSerialTester.Core;
using SimpleSerialTester.ViewModels;

namespace SimpleSerialTester.Tests;

/// <summary>
/// 画面の裏側（MainViewModel）を、ループバックにつないで実際に動かす。
/// 画面と同じく、専用のスレッドでメッセージを回しながら確かめる。
/// </summary>
public class MainViewModelTests
{
    [Fact]
    public void 送ったものが送信と受信の2行になり履歴に残る()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);

            Assert.True(vm.Send("VER?", isHex: false, addToHistory: true));
            PumpUntil(() => Lines(vm, LogDirection.Received).Count == 1);

            Assert.Equal(["VER?\\r\\n"], Lines(vm, LogDirection.Sent));
            Assert.Equal(["VER?"], Lines(vm, LogDirection.Received));
            Assert.Equal("VER?", Assert.Single(vm.History).Data);
            Assert.Equal(6, vm.TxBytes);
            Assert.Equal(6, vm.RxBytes);
        });
    }

    [Fact]
    public void 断片に分かれて返ってきても受信は1行にまとまる()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);

            vm.Send("TEMP=25.0", isHex: false, addToHistory: false);
            vm.Send("HUMID=40", isHex: false, addToHistory: false);
            PumpUntil(() => Lines(vm, LogDirection.Received).Count == 2);

            Assert.Equal(["TEMP=25.0", "HUMID=40"], Lines(vm, LogDirection.Received));
        });
    }

    [Fact]
    public void 区切り文字のないバイナリは無通信の時間で1行になる()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.ShowHex = true;

            vm.Send("02 52 53 54 03", isHex: true, addToHistory: false);
            PumpUntil(() => Lines(vm, LogDirection.Received).Count == 1);

            Assert.Equal(["02 52 53 54 03"], Lines(vm, LogDirection.Received));
            Assert.True(vm.Log.Last(entry => entry.Direction == LogDirection.Received).Model.Unterminated);
        });
    }

    [Fact]
    public void 同じ内容をもう一度送っても履歴は1件のまま先頭へ移る()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);

            vm.Send("A", isHex: false, addToHistory: true);
            vm.Send("B", isHex: false, addToHistory: true);
            vm.Send("A", isHex: false, addToHistory: true);

            Assert.Equal(["A", "B"], vm.History.Select(item => item.Data));
        });
    }

    [Fact]
    public void 履歴を選ぶと入力欄へ写り上下キーでたどれる()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.Send("FIRST", isHex: false, addToHistory: true);
            vm.Send("02 03", isHex: true, addToHistory: true);

            vm.SelectedHistory = vm.History[1];
            Assert.Equal("FIRST", vm.SendText);
            Assert.False(vm.SendAsHex);

            vm.SendText = "";
            vm.RecallHistory(1);
            Assert.Equal("02 03", vm.SendText);
            Assert.True(vm.SendAsHex);
            vm.RecallHistory(1);
            Assert.Equal("FIRST", vm.SendText);
            vm.RecallHistory(-1);
            vm.RecallHistory(-1);
            Assert.Equal("", vm.SendText);
        });
    }

    [Fact]
    public void 読めないHEXと未接続は送らずに理由を出す()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = new MainViewModel(dispatcher, settingsPath: null);

            Assert.False(vm.Send("AT", isHex: false, addToHistory: true));
            Assert.True(vm.StatusIsError);
            Assert.Empty(vm.History);

            vm.SelectedPortName = PortSettings.LoopbackName;
            Assert.True(vm.Connect());
            Assert.False(vm.Send("0G", isHex: true, addToHistory: true));
            Assert.True(vm.StatusIsError);
            Assert.Empty(Lines(vm, LogDirection.Sent));
            Assert.Empty(vm.History);
        });
    }

    [Fact]
    public void ボーレートが数値でなければ接続しない()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = new MainViewModel(dispatcher, settingsPath: null);
            vm.SelectedPortName = PortSettings.LoopbackName;
            vm.BaudRateText = "fast";

            Assert.False(vm.Connect());
            Assert.False(vm.IsConnected);
            Assert.True(vm.StatusIsError);
        });
    }

    [Fact]
    public void 自動応答は当てはまる受信にだけ決めた内容を送る()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.AutoReplies.Add(new AutoReplyRuleViewModel { Contains = "NG", Reply = "RETRY" });
            vm.AutoReplyEnabled = true;

            vm.Send("OK", isHex: false, addToHistory: false);
            vm.Send("NG 3", isHex: false, addToHistory: false);
            PumpUntil(() => Lines(vm, LogDirection.Received).Count == 3);

            // 折り返しなので、送った「NG 3」が受信になり、それに反応して「RETRY」を送る。
            Assert.Equal(["OK\\r\\n", "NG 3\\r\\n", "RETRY\\r\\n"], Lines(vm, LogDirection.Sent));
            Assert.Equal(["OK", "NG 3", "RETRY"], Lines(vm, LogDirection.Received));
            Assert.Empty(vm.History);
        });
    }

    [Fact]
    public void 応答が応答を呼び続けたら自動応答を止める()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.AutoReplies.Add(new AutoReplyRuleViewModel { Contains = "PING", Reply = "PING" });
            vm.AutoReplyEnabled = true;

            vm.Send("PING", isHex: false, addToHistory: false);
            PumpUntil(() => !vm.AutoReplyEnabled, timeoutMs: 8000);

            Assert.False(vm.AutoReplyEnabled);
            Assert.True(vm.StatusIsError);
            Assert.InRange(Lines(vm, LogDirection.Sent).Count, 21, 22);
        });
    }

    [Fact]
    public void 繰り返し送信は止めるまで同じ内容を送り続ける()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.SendText = "POLL";
            vm.RepeatIntervalText = "50";

            vm.ToggleRepeatCommand.Execute(null);
            Assert.True(vm.IsRepeating);
            PumpUntil(() => Lines(vm, LogDirection.Sent).Count >= 4);
            vm.ToggleRepeatCommand.Execute(null);

            Assert.False(vm.IsRepeating);
            var sent = Lines(vm, LogDirection.Sent);
            Assert.All(sent, line => Assert.Equal("POLL\\r\\n", line));
            Assert.Equal("POLL", Assert.Single(vm.History).Data);

            var countAfterStop = sent.Count;
            PumpFor(200);
            Assert.Equal(countAfterStop, Lines(vm, LogDirection.Sent).Count);
        });
    }

    [Fact]
    public void 登録リストを上から順に1回ずつ送る()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.AddFavorite("VER?", isHex: false);
            vm.AddFavorite("02 41 03", isHex: true);
            vm.AddFavorite("STATUS?", isHex: false);
            vm.SequenceIntervalText = "50";

            vm.ToggleSequenceCommand.Execute(null);
            Assert.True(vm.IsSequenceRunning);
            PumpUntil(() => !vm.IsSequenceRunning);

            Assert.Equal(["VER?\\r\\n", "\\x02A\\x03", "STATUS?\\r\\n"], Lines(vm, LogDirection.Sent));
        });
    }

    [Fact]
    public void 切断すると繰り返し送信も止まる()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.SendText = "POLL";
            vm.RepeatIntervalText = "50";
            vm.ToggleRepeatCommand.Execute(null);

            vm.ToggleConnect();

            Assert.False(vm.IsConnected);
            Assert.False(vm.IsRepeating);
            Assert.Equal("切断中", vm.ConnectionText);
        });
    }

    [Fact]
    public void 終了時に保存した内容が次の起動で戻る()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sst-vm-{Guid.NewGuid():N}", "settings.json");
        try
        {
            RunOnUiThread(dispatcher =>
            {
                using var vm = new MainViewModel(dispatcher, path);
                vm.SelectedPortName = PortSettings.LoopbackName;
                vm.BaudRateText = "115200";
                vm.ShowHex = true;
                vm.AddFavorite("VER?", isHex: false);
                vm.Favorites[0].Name = "バージョン確認";
                vm.AutoReplies.Add(new AutoReplyRuleViewModel { Contains = "NG", Reply = "RETRY" });
                Assert.True(vm.Connect());
                vm.Send("HELLO", isHex: false, addToHistory: true);
                vm.SaveSettings();
            });

            RunOnUiThread(dispatcher =>
            {
                using var vm = new MainViewModel(dispatcher, path);

                Assert.Equal(PortSettings.LoopbackName, vm.SelectedPortName);
                Assert.Equal("115200", vm.BaudRateText);
                Assert.True(vm.ShowHex);
                Assert.Equal("バージョン確認", Assert.Single(vm.Favorites).Name);
                Assert.Equal("NG", Assert.Single(vm.AutoReplies).Contains);
                Assert.Equal("HELLO", Assert.Single(vm.History).Data);
                Assert.False(vm.IsConnected);
            });
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void 表示をHEXへ切り替えると既にあるログも書き換わる()
    {
        RunOnUiThread(dispatcher =>
        {
            using var vm = NewConnected(dispatcher);
            vm.Send("AB", isHex: false, addToHistory: false);

            Assert.Equal(["AB\\r\\n"], Lines(vm, LogDirection.Sent));
            vm.ShowHex = true;
            Assert.Equal(["41 42 0D 0A"], Lines(vm, LogDirection.Sent));
        });
    }

    // ---- 道具 ----

    private static MainViewModel NewConnected(Dispatcher dispatcher)
    {
        var vm = new MainViewModel(dispatcher, settingsPath: null) { SelectedPortName = PortSettings.LoopbackName };
        Assert.True(vm.Connect());
        return vm;
    }

    private static List<string> Lines(MainViewModel vm, LogDirection direction)
        => vm.Log.Where(entry => entry.Direction == direction).Select(entry => entry.Body).ToList();

    private static void RunOnUiThread(Action<Dispatcher> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>条件が成り立つまで、画面のスレッドのメッセージを回して待つ。</summary>
    private static void PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMs)
        {
            PumpOnce();
        }

        Assert.True(condition(), $"{timeoutMs} ミリ秒待っても条件が成り立たなかった。");
    }

    private static void PumpFor(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            PumpOnce();
        }
    }

    private static void PumpOnce()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        Thread.Sleep(2);
    }
}
