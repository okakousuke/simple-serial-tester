using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using SimpleSerialTester.Core;

namespace SimpleSerialTester.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int LogLimit = 5000;

    private readonly Dispatcher _dispatcher;
    private readonly string? _settingsPath;
    private readonly FrameAssembler _assembler = new();
    private readonly ReplyLimiter _replyLimiter = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _repeatTimer;

    private ISerialTransport? _transport;
    private CancellationTokenSource? _sequenceCts;
    private int _historyCursor = -1;

    private string _selectedPortName = "";
    private string _baudRateText = "9600";
    private int _dataBits = 8;
    private ParityKind _parity = ParityKind.None;
    private StopBitsKind _stopBits = StopBitsKind.One;
    private LineEnding _sendLineEnding = LineEnding.CRLF;
    private LineEnding _receiveLineEnding = LineEnding.CRLF;
    private string _idleTimeoutText = "50";
    private bool _isConnected;
    private string _connectionText = "切断中";
    private string _sendText = "";
    private bool _sendAsHex;
    private bool _clearInputAfterSend;
    private bool _showHex;
    private bool _autoScroll = true;
    private long _txBytes;
    private long _rxBytes;
    private string _statusMessage = "";
    private bool _statusIsError;
    private SendItem? _selectedHistory;
    private SendItemViewModel? _selectedFavorite;
    private string _repeatIntervalText = "1000";
    private bool _isRepeating;
    private string _sequenceIntervalText = "500";
    private bool _isSequenceRunning;
    private bool _sequenceLoop;
    private bool _autoReplyEnabled;

    public MainViewModel(Dispatcher dispatcher, string? settingsPath)
    {
        _dispatcher = dispatcher;
        _settingsPath = settingsPath;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
        _flushTimer.Tick += (_, _) => FlushIdleFrame();
        _repeatTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _repeatTimer.Tick += (_, _) => RepeatTick();

        ToggleConnectCommand = new RelayCommand(ToggleConnect);
        RefreshPortsCommand = new RelayCommand(() => RefreshPorts(SelectedPortName), () => !IsConnected);
        SendCommand = new RelayCommand(SendFromInput, () => IsConnected);
        ClearLogCommand = new RelayCommand(ClearLog);
        ClearHistoryCommand = new RelayCommand(() => History.Clear(), () => History.Count > 0);
        AddFavoriteCommand = new RelayCommand(AddFavoriteFromInput, () => !string.IsNullOrEmpty(SendText));
        RemoveFavoriteCommand = new RelayCommand(RemoveSelectedFavorite, () => SelectedFavorite is not null);
        MoveFavoriteUpCommand = new RelayCommand(() => MoveSelectedFavorite(-1), () => CanMoveFavorite(-1));
        MoveFavoriteDownCommand = new RelayCommand(() => MoveSelectedFavorite(1), () => CanMoveFavorite(1));
        ToggleRepeatCommand = new RelayCommand(ToggleRepeat, () => IsConnected);
        ToggleSequenceCommand = new RelayCommand(ToggleSequence, () => IsConnected && (IsSequenceRunning || Favorites.Count > 0));
        AddRuleCommand = new RelayCommand(() => AutoReplies.Add(new AutoReplyRuleViewModel()));
        RemoveRuleCommand = new RelayCommand(rule =>
        {
            if (rule is AutoReplyRuleViewModel target)
            {
                AutoReplies.Remove(target);
            }
        });

        ApplySettings(settingsPath is null ? new AppSettings() : SettingsStore.Load(settingsPath));
    }

    // ---- 選択肢 ----

    public ObservableCollection<Choice<string>> Ports { get; } = [];

    public IReadOnlyList<string> BaudRates { get; } =
        ["1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600"];

    public IReadOnlyList<int> DataBitsChoices { get; } = [5, 6, 7, 8];

    public IReadOnlyList<Choice<ParityKind>> ParityChoices { get; } =
    [
        new(ParityKind.None, "なし"),
        new(ParityKind.Odd, "奇数"),
        new(ParityKind.Even, "偶数"),
        new(ParityKind.Mark, "マーク"),
        new(ParityKind.Space, "スペース"),
    ];

    public IReadOnlyList<Choice<StopBitsKind>> StopBitsChoices { get; } =
    [
        new(StopBitsKind.One, "1"),
        new(StopBitsKind.OnePointFive, "1.5"),
        new(StopBitsKind.Two, "2"),
    ];

    public IReadOnlyList<Choice<LineEnding>> SendLineEndingChoices { get; } =
    [
        new(LineEnding.CRLF, "CR+LF"),
        new(LineEnding.CR, "CR"),
        new(LineEnding.LF, "LF"),
        new(LineEnding.None, "なし"),
    ];

    public IReadOnlyList<Choice<LineEnding>> ReceiveLineEndingChoices { get; } =
    [
        new(LineEnding.CRLF, "CR+LF"),
        new(LineEnding.CR, "CR"),
        new(LineEnding.LF, "LF"),
        new(LineEnding.None, "時間だけ"),
    ];

    // ---- 接続 ----

    public string SelectedPortName
    {
        get => _selectedPortName;
        set => Set(ref _selectedPortName, value ?? "");
    }

    public string BaudRateText
    {
        get => _baudRateText;
        set => Set(ref _baudRateText, value ?? "");
    }

    public int DataBits
    {
        get => _dataBits;
        set => Set(ref _dataBits, value);
    }

    public ParityKind Parity
    {
        get => _parity;
        set => Set(ref _parity, value);
    }

    public StopBitsKind StopBits
    {
        get => _stopBits;
        set => Set(ref _stopBits, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
            {
                Raise(nameof(IsDisconnected));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    /// <summary>「COM3 9600 8N1」や「切断中」。</summary>
    public string ConnectionText
    {
        get => _connectionText;
        private set => Set(ref _connectionText, value);
    }

    // ---- 送信 ----

    public string SendText
    {
        get => _sendText;
        set
        {
            // 履歴から写したときなど、キー入力以外で変わった場合もボタンの有効・無効を更新する。
            if (Set(ref _sendText, value ?? ""))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool SendAsHex
    {
        get => _sendAsHex;
        set => Set(ref _sendAsHex, value);
    }

    public LineEnding SendLineEnding
    {
        get => _sendLineEnding;
        set => Set(ref _sendLineEnding, value);
    }

    public bool ClearInputAfterSend
    {
        get => _clearInputAfterSend;
        set => Set(ref _clearInputAfterSend, value);
    }

    // ---- 受信とログ ----

    public LineEnding ReceiveLineEnding
    {
        get => _receiveLineEnding;
        set => Set(ref _receiveLineEnding, value);
    }

    /// <summary>無通信で区切るまでの時間（ミリ秒）。</summary>
    public string IdleTimeoutText
    {
        get => _idleTimeoutText;
        set => Set(ref _idleTimeoutText, value ?? "");
    }

    public ObservableCollection<LogEntryViewModel> Log { get; } = [];

    public bool ShowHex
    {
        get => _showHex;
        set
        {
            if (Set(ref _showHex, value))
            {
                foreach (var entry in Log)
                {
                    entry.SetHex(value);
                }
            }
        }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => Set(ref _autoScroll, value);
    }

    public long TxBytes
    {
        get => _txBytes;
        private set
        {
            if (Set(ref _txBytes, value))
            {
                Raise(nameof(CounterText));
            }
        }
    }

    public long RxBytes
    {
        get => _rxBytes;
        private set
        {
            if (Set(ref _rxBytes, value))
            {
                Raise(nameof(CounterText));
            }
        }
    }

    public string CounterText => $"送信 {TxBytes:N0} バイト　受信 {RxBytes:N0} バイト";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => Set(ref _statusIsError, value);
    }

    // ---- 履歴と登録リスト ----

    public ObservableCollection<SendItem> History { get; } = [];

    /// <summary>履歴で選んだものは、すぐ入力欄へ写す（選んで直して送る、が1手で済む）。</summary>
    public SendItem? SelectedHistory
    {
        get => _selectedHistory;
        set
        {
            if (Set(ref _selectedHistory, value) && value is not null)
            {
                UseItem(value.Data, value.IsHex);
            }
        }
    }

    public ObservableCollection<SendItemViewModel> Favorites { get; } = [];

    public SendItemViewModel? SelectedFavorite
    {
        get => _selectedFavorite;
        set
        {
            if (Set(ref _selectedFavorite, value))
            {
                Raise(nameof(HasSelectedFavorite));
            }
        }
    }

    public bool HasSelectedFavorite => SelectedFavorite is not null;

    // ---- 簡易マクロ ----

    public string RepeatIntervalText
    {
        get => _repeatIntervalText;
        set => Set(ref _repeatIntervalText, value ?? "");
    }

    public bool IsRepeating
    {
        get => _isRepeating;
        private set => Set(ref _isRepeating, value);
    }

    public string SequenceIntervalText
    {
        get => _sequenceIntervalText;
        set => Set(ref _sequenceIntervalText, value ?? "");
    }

    public bool IsSequenceRunning
    {
        get => _isSequenceRunning;
        private set => Set(ref _isSequenceRunning, value);
    }

    public bool SequenceLoop
    {
        get => _sequenceLoop;
        set => Set(ref _sequenceLoop, value);
    }

    public bool AutoReplyEnabled
    {
        get => _autoReplyEnabled;
        set
        {
            if (Set(ref _autoReplyEnabled, value) && value)
            {
                _replyLimiter.Reset();
            }
        }
    }

    public ObservableCollection<AutoReplyRuleViewModel> AutoReplies { get; } = [];

    // ---- コマンド ----

    public ICommand ToggleConnectCommand { get; }

    public ICommand RefreshPortsCommand { get; }

    public ICommand SendCommand { get; }

    public ICommand ClearLogCommand { get; }

    public ICommand ClearHistoryCommand { get; }

    public ICommand AddFavoriteCommand { get; }

    public ICommand RemoveFavoriteCommand { get; }

    public ICommand MoveFavoriteUpCommand { get; }

    public ICommand MoveFavoriteDownCommand { get; }

    public ICommand ToggleRepeatCommand { get; }

    public ICommand ToggleSequenceCommand { get; }

    public ICommand AddRuleCommand { get; }

    public ICommand RemoveRuleCommand { get; }

    // ---- 接続の操作 ----

    public void RefreshPorts(string? preferred)
    {
        Ports.Clear();
        foreach (var name in SerialPortTransport.GetPortNames())
        {
            Ports.Add(new Choice<string>(name, name));
        }

        Ports.Add(new Choice<string>(PortSettings.LoopbackName, "ループバック（機器なしで試す）"));

        SelectedPortName = Ports.Any(port => port.Value == preferred) ? preferred! : Ports[0].Value;
    }

    public void ToggleConnect()
    {
        if (IsConnected)
        {
            Disconnect("切断しました。");
        }
        else
        {
            Connect();
        }
    }

    public bool Connect()
    {
        if (IsConnected)
        {
            return true;
        }

        if (string.IsNullOrEmpty(SelectedPortName))
        {
            return Fail("ポートを選んでください。");
        }

        if (!int.TryParse(BaudRateText, NumberStyles.None, CultureInfo.InvariantCulture, out var baudRate) || baudRate <= 0)
        {
            return Fail("ボーレートは正の整数で指定してください。");
        }

        var settings = new PortSettings(SelectedPortName, baudRate, DataBits, Parity, StopBits);
        ISerialTransport transport = settings.IsLoopback ? new LoopbackTransport() : new SerialPortTransport(settings);
        transport.DataReceived += (data, time) => _dispatcher.BeginInvoke(() => HandleChunk(transport, data, time));
        transport.Faulted += message => _dispatcher.BeginInvoke(() => HandleFault(transport, message));

        try
        {
            transport.Open();
        }
        catch (Exception ex)
        {
            transport.Dispose();
            return Fail($"接続できませんでした: {ex.Message}");
        }

        _transport = transport;
        _assembler.Clear();
        _replyLimiter.Reset();
        IsConnected = true;
        ConnectionText = settings.IsLoopback ? "ループバック" : $"{settings.PortName}  {settings.Summary}";
        AddInfo(settings.IsLoopback
            ? "ループバックに接続しました。送ったものが、そのまま受信として返ります。"
            : $"{settings.PortName} に接続しました（{settings.Summary}）。");
        SetStatus("");
        _flushTimer.Start();
        return true;
    }

    public void Disconnect(string message)
    {
        if (!IsConnected)
        {
            return;
        }

        StopRepeat();
        StopSequence();
        _flushTimer.Stop();

        if (_assembler.FlushAll() is { } rest)
        {
            AddReceived(rest);
        }

        var transport = _transport;
        _transport = null;
        transport?.Dispose();

        IsConnected = false;
        ConnectionText = "切断中";
        AddInfo(message);
    }

    // ---- 送信の操作 ----

    public bool Send(string text, bool isHex, bool addToHistory)
    {
        var transport = _transport;
        if (transport is not { IsOpen: true })
        {
            return Fail("接続していません。");
        }

        if (!PayloadBuilder.TryBuild(text, isHex, SendLineEnding, out var payload, out var error))
        {
            return Fail(error);
        }

        try
        {
            transport.Write(payload);
        }
        catch (Exception ex)
        {
            return Fail($"送信できませんでした: {ex.Message}");
        }

        TxBytes += payload.Length;
        Append(new LogEntry(DateTime.Now, LogDirection.Sent, payload));
        if (addToHistory)
        {
            SendHistory.Push(History, text, isHex);
            _historyCursor = -1;
        }

        SetStatus("");
        return true;
    }

    public void UseItem(string data, bool isHex)
    {
        SendText = data;
        SendAsHex = isHex;
    }

    /// <summary>入力欄で上下キーを押したときに、履歴を順にたどる。</summary>
    public void RecallHistory(int step)
    {
        if (History.Count == 0)
        {
            return;
        }

        _historyCursor = Math.Clamp(_historyCursor + step, -1, History.Count - 1);
        if (_historyCursor < 0)
        {
            SendText = "";
            return;
        }

        var item = History[_historyCursor];
        UseItem(item.Data, item.IsHex);
    }

    public void AddFavorite(string data, bool isHex)
    {
        var item = new SendItemViewModel { Data = data, IsHex = isHex };
        Favorites.Add(item);
        SelectedFavorite = item;
    }

    // ---- ログの操作 ----

    public string FormatForCopy(IEnumerable<LogEntryViewModel> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.AppendLine(LogFormatter.FileLine(entry.Model, ShowHex));
        }

        return sb.ToString();
    }

    public void SaveLog(string path)
    {
        try
        {
            File.WriteAllText(path, FormatForCopy(Log), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus($"ログを保存しました: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"ログを保存できませんでした: {ex.Message}");
        }
    }

    public void SaveSettings()
    {
        if (_settingsPath is null)
        {
            return;
        }

        var settings = new AppSettings
        {
            PortName = SelectedPortName,
            BaudRate = int.TryParse(BaudRateText, out var baudRate) && baudRate > 0 ? baudRate : 9600,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
            SendLineEnding = SendLineEnding,
            ReceiveLineEnding = ReceiveLineEnding,
            IdleTimeoutMs = ParseClamped(IdleTimeoutText, 50, 5, 5000),
            SendAsHex = SendAsHex,
            ShowHex = ShowHex,
            ClearInputAfterSend = ClearInputAfterSend,
            AutoScroll = AutoScroll,
            RepeatIntervalMs = ParseClamped(RepeatIntervalText, 1000, 50, 60000),
            SequenceIntervalMs = ParseClamped(SequenceIntervalText, 500, 50, 60000),
            AutoReplyEnabled = AutoReplyEnabled,
            History = History.ToList(),
            Favorites = Favorites.Select(item => item.ToModel()).ToList(),
            AutoReplies = AutoReplies.Select(rule => rule.ToModel()).ToList(),
        };

        try
        {
            SettingsStore.Save(_settingsPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 設定を保存できなくても、試験の作業は続けられる。次回は既定値か、前回までの設定で始まる。
        }
    }

    public void Dispose()
    {
        Disconnect("切断しました。");
        _flushTimer.Stop();
        _repeatTimer.Stop();
    }

    // ---- 内部 ----

    private void ApplySettings(AppSettings settings)
    {
        BaudRateText = settings.BaudRate.ToString(CultureInfo.InvariantCulture);
        DataBits = DataBitsChoices.Contains(settings.DataBits) ? settings.DataBits : 8;
        Parity = settings.Parity;
        StopBits = settings.StopBits;
        SendLineEnding = settings.SendLineEnding;
        ReceiveLineEnding = settings.ReceiveLineEnding;
        IdleTimeoutText = settings.IdleTimeoutMs.ToString(CultureInfo.InvariantCulture);
        SendAsHex = settings.SendAsHex;
        ShowHex = settings.ShowHex;
        ClearInputAfterSend = settings.ClearInputAfterSend;
        AutoScroll = settings.AutoScroll;
        RepeatIntervalText = settings.RepeatIntervalMs.ToString(CultureInfo.InvariantCulture);
        SequenceIntervalText = settings.SequenceIntervalMs.ToString(CultureInfo.InvariantCulture);
        AutoReplyEnabled = settings.AutoReplyEnabled;

        foreach (var item in settings.History)
        {
            History.Add(item);
        }

        foreach (var item in settings.Favorites)
        {
            Favorites.Add(new SendItemViewModel(item));
        }

        foreach (var rule in settings.AutoReplies)
        {
            AutoReplies.Add(new AutoReplyRuleViewModel(rule));
        }

        RefreshPorts(settings.PortName);
    }

    private void SendFromInput()
    {
        if (Send(SendText, SendAsHex, addToHistory: true) && ClearInputAfterSend)
        {
            SendText = "";
        }
    }

    private void HandleChunk(ISerialTransport source, byte[] data, DateTime time)
    {
        // 切断したあとに遅れて届いた通知は捨てる。
        if (!ReferenceEquals(source, _transport))
        {
            return;
        }

        _assembler.Terminator = ReceiveLineEnding;
        _assembler.IdleTimeout = TimeSpan.FromMilliseconds(ParseClamped(IdleTimeoutText, 50, 5, 5000));
        RxBytes += data.Length;

        foreach (var frame in _assembler.Push(data, time))
        {
            AddReceived(frame);
        }
    }

    private void HandleFault(ISerialTransport source, string message)
    {
        if (ReferenceEquals(source, _transport))
        {
            Disconnect($"受信中に異常が起きたため、切断しました: {message}");
            SetStatus("受信中に異常が起きたため、切断しました。", isError: true);
        }
    }

    private void FlushIdleFrame()
    {
        if (_assembler.FlushIfIdle(DateTime.Now) is { } frame)
        {
            AddReceived(frame);
        }
    }

    private void AddReceived(Frame frame)
    {
        // 「時間だけ」で区切る設定では、区切り文字が無いのが普通なので、補足を付けない。
        var unterminated = !frame.Terminated && ReceiveLineEnding != LineEnding.None;
        Append(new LogEntry(frame.StartedAt, LogDirection.Received, frame.Data, unterminated));

        if (!AutoReplyEnabled || !IsConnected)
        {
            return;
        }

        var text = TextCodec.ToDisplay(frame.Data);
        var rules = AutoReplies.Select(rule => rule.ToModel()).ToList();
        foreach (var rule in AutoReplyEngine.Match(rules, text))
        {
            if (!_replyLimiter.TryAcquire(DateTime.Now))
            {
                AutoReplyEnabled = false;
                AddInfo("自動応答が1秒に20回を超えたため、止めました。応答が応答を呼んでいないか確かめてください。");
                SetStatus("自動応答を止めました。", isError: true);
                return;
            }

            if (!Send(rule.Reply, rule.ReplyIsHex, addToHistory: false))
            {
                return;
            }
        }
    }

    private void ToggleRepeat()
    {
        if (IsRepeating)
        {
            StopRepeat();
            AddInfo("繰り返し送信を止めました。");
            return;
        }

        var interval = ParseClamped(RepeatIntervalText, 1000, 50, 60000);
        RepeatIntervalText = interval.ToString(CultureInfo.InvariantCulture);
        if (!Send(SendText, SendAsHex, addToHistory: true))
        {
            return;
        }

        _repeatTimer.Interval = TimeSpan.FromMilliseconds(interval);
        _repeatTimer.Start();
        IsRepeating = true;
        AddInfo($"繰り返し送信を始めました（{interval} ミリ秒ごと）。");
    }

    private void RepeatTick()
    {
        if (!Send(SendText, SendAsHex, addToHistory: false))
        {
            StopRepeat();
        }
    }

    private void StopRepeat()
    {
        _repeatTimer.Stop();
        IsRepeating = false;
    }

    private async void ToggleSequence()
    {
        if (IsSequenceRunning)
        {
            StopSequence();
            return;
        }

        var items = Favorites.Select(item => item.ToModel()).Where(item => item.Data.Length > 0).ToList();
        if (items.Count == 0)
        {
            Fail("登録リストが空です。");
            return;
        }

        var interval = ParseClamped(SequenceIntervalText, 500, 50, 60000);
        SequenceIntervalText = interval.ToString(CultureInfo.InvariantCulture);
        var cts = new CancellationTokenSource();
        _sequenceCts = cts;
        IsSequenceRunning = true;
        AddInfo($"登録リストを順に送ります（{items.Count} 件、{interval} ミリ秒おき）。");

        try
        {
            do
            {
                foreach (var item in items)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (!Send(item.Data, item.IsHex, addToHistory: false))
                    {
                        return;
                    }

                    await Task.Delay(interval, cts.Token);
                }
            }
            while (SequenceLoop);

            AddInfo("登録リストを最後まで送りました。");
        }
        catch (OperationCanceledException)
        {
            AddInfo("登録リストの送信を止めました。");
        }
        finally
        {
            if (ReferenceEquals(_sequenceCts, cts))
            {
                _sequenceCts = null;
                IsSequenceRunning = false;
            }

            cts.Dispose();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void StopSequence()
    {
        _sequenceCts?.Cancel();
    }

    private void AddFavoriteFromInput()
    {
        if (!string.IsNullOrEmpty(SendText))
        {
            AddFavorite(SendText, SendAsHex);
        }
    }

    private void RemoveSelectedFavorite()
    {
        if (SelectedFavorite is not { } target)
        {
            return;
        }

        var index = Favorites.IndexOf(target);
        Favorites.Remove(target);
        SelectedFavorite = Favorites.Count == 0 ? null : Favorites[Math.Min(index, Favorites.Count - 1)];
    }

    private bool CanMoveFavorite(int step)
    {
        if (SelectedFavorite is not { } target)
        {
            return false;
        }

        var destination = Favorites.IndexOf(target) + step;
        return destination >= 0 && destination < Favorites.Count;
    }

    private void MoveSelectedFavorite(int step)
    {
        if (SelectedFavorite is { } target && CanMoveFavorite(step))
        {
            var index = Favorites.IndexOf(target);
            Favorites.Move(index, index + step);
        }
    }

    private void ClearLog()
    {
        Log.Clear();
        TxBytes = 0;
        RxBytes = 0;
    }

    private void Append(LogEntry entry)
    {
        Log.Add(new LogEntryViewModel(entry, ShowHex));
        while (Log.Count > LogLimit)
        {
            Log.RemoveAt(0);
        }
    }

    private void AddInfo(string message) => Append(LogEntry.Info(DateTime.Now, message));

    private bool Fail(string message)
    {
        SetStatus(message, isError: true);
        return false;
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private static int ParseClamped(string text, int fallback, int min, int max)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}
