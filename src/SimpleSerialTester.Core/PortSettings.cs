namespace SimpleSerialTester.Core;

public enum ParityKind
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

public enum StopBitsKind
{
    One,
    OnePointFive,
    Two,
}

/// <summary>接続の設定。</summary>
public sealed record PortSettings(string PortName, int BaudRate, int DataBits, ParityKind Parity, StopBitsKind StopBits)
{
    /// <summary>機器なしで試すための、折り返し用の仮のポート名。</summary>
    public const string LoopbackName = "LOOPBACK";

    public bool IsLoopback => PortName == LoopbackName;

    /// <summary>「9600 8N1」の形の要約。</summary>
    public string Summary
    {
        get
        {
            var parity = Parity switch
            {
                ParityKind.Odd => "O",
                ParityKind.Even => "E",
                ParityKind.Mark => "M",
                ParityKind.Space => "S",
                _ => "N",
            };
            var stop = StopBits switch
            {
                StopBitsKind.OnePointFive => "1.5",
                StopBitsKind.Two => "2",
                _ => "1",
            };
            return $"{BaudRate} {DataBits}{parity}{stop}";
        }
    }
}
