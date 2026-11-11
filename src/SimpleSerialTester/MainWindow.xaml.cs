using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SimpleSerialTester.Core;
using SimpleSerialTester.ViewModels;

namespace SimpleSerialTester;

public partial class MainWindow : Window
{
    private bool _scrollPending;

    public MainWindow(string? settingsPath)
    {
        InitializeComponent();

        Vm = new MainViewModel(Dispatcher, settingsPath);
        DataContext = Vm;
        Vm.Log.CollectionChanged += Log_CollectionChanged;
        Closing += (_, _) =>
        {
            Vm.SaveSettings();
            Vm.Dispose();
        };
        Loaded += (_, _) => SendBox.Focus();
    }

    public MainViewModel Vm { get; }

    /// <summary>説明用の画面を作る。架空の命令をループバックへ送り、画面の各部に中身を入れる。</summary>
    /// <param name="tabIndex">左側で開いておくタブ（0：履歴、1：登録リスト、2：かんたんマクロ）。</param>
    public async Task RunDemoAsync(int tabIndex = 0)
    {
        Vm.SelectedPortName = PortSettings.LoopbackName;
        Vm.AddFavorite("VER?", isHex: false);
        Vm.Favorites[^1].Name = "バージョン確認";
        Vm.AddFavorite("STATUS?", isHex: false);
        Vm.Favorites[^1].Name = "状態を読む";
        Vm.AddFavorite("02 52 53 54 03", isHex: true);
        Vm.Favorites[^1].Name = "リセット（バイナリ）";
        Vm.SelectedFavorite = tabIndex == 1 ? Vm.Favorites[0] : null;
        Vm.AutoReplies.Add(new AutoReplyRuleViewModel { Contains = "NG", Reply = "RETRY" });
        Vm.AutoReplies.Add(new AutoReplyRuleViewModel { Contains = "READY", Reply = "02 53 03", ReplyIsHex = true, Enabled = false });
        SideTabs.SelectedIndex = Math.Clamp(tabIndex, 0, SideTabs.Items.Count - 1);

        if (!Vm.Connect())
        {
            return;
        }

        foreach (var (text, isHex) in new[] { ("VER?", false), ("STATUS?", false), ("TEMP=25.0", false), ("02 52 53 54 03", true) })
        {
            Vm.Send(text, isHex, addToHistory: true);
            await Task.Delay(120);
        }

        Vm.SendText = "STATUS?";
        Vm.SendAsHex = false;
        await Task.Delay(200);
    }

    private void Log_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || !Vm.AutoScroll || _scrollPending)
        {
            return;
        }

        // この通知の中で一覧を送ってはいけない。一覧のほうがまだ追加を処理し終えていないことがあり、
        // その状態で画面を計算し直すと、WPFが食い違いを見つけて例外を投げる。
        // 通知がすべて終わってから送る。続けて何行も来たときは、1回にまとめる。
        _scrollPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollPending = false;
            if (Vm.AutoScroll && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }
        });
    }

    // ---- 送信欄 ----

    private void SendBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 端末と同じく、上で古い履歴へ、下で新しい履歴へ。
        if (e.Key == Key.Up)
        {
            Vm.RecallHistory(1);
            SendBox.CaretIndex = SendBox.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Vm.RecallHistory(-1);
            SendBox.CaretIndex = SendBox.Text.Length;
            e.Handled = true;
        }
    }

    // ---- 履歴 ----

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is SendItem item)
        {
            Vm.Send(item.Data, item.IsHex, addToHistory: true);
        }
    }

    private void HistorySend_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Vm.Send(item.Data, item.IsHex, addToHistory: true);
        }
    }

    private void HistoryUse_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Vm.UseItem(item.Data, item.IsHex);
            SendBox.Focus();
            SendBox.CaretIndex = SendBox.Text.Length;
        }
    }

    private void HistoryCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            SetClipboard(item.Data);
        }
    }

    private void HistoryAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Vm.AddFavorite(item.Data, item.IsHex);
        }
    }

    private void HistoryRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Vm.History.Remove(item);
        }
    }

    private static SendItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as SendItem;

    // ---- 登録リスト ----

    private void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FavoritesList.SelectedItem is SendItemViewModel item)
        {
            Vm.Send(item.Data, item.IsHex, addToHistory: true);
        }
    }

    // ---- ログ ----

    private void CopyLog_Click(object sender, RoutedEventArgs e) => CopySelectedLog();

    private void LogList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelectedLog();
            e.Handled = true;
        }
    }

    private void CopySelectedLog()
    {
        // 選んだ順ではなく、ログに並んでいる順でコピーする。
        var selected = LogList.SelectedItems.Cast<LogEntryViewModel>().ToHashSet();
        var entries = selected.Count > 0 ? Vm.Log.Where(selected.Contains) : Vm.Log;
        SetClipboard(Vm.FormatForCopy(entries));
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "ログを保存",
            Filter = "テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            FileName = $"serial-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        if (dialog.ShowDialog(this) == true)
        {
            Vm.SaveLog(dialog.FileName);
        }
    }

    private static void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // ほかのアプリがクリップボードを使っている最中だと失敗する。もう一度押せば通るので、ここでは何もしない。
        }
    }
}
