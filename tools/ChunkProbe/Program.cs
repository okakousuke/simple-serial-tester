// 受信の通知が、電文の切れ目で来るかどうかを確かめる。
//
//   dotnet run --project tools/ChunkProbe                                  … ループバック（機器なし）
//   dotnet run --project tools/ChunkProbe -- --port COM7 --baud 9600       … 実際のポート（TXとRXを折り返しておく）
//   オプション: --lines 5（送る行数） --length 40（1行の長さ） --gap 30（行と行の間のミリ秒）
//
// 同じ受信データを、(1) 通知が来た単位のまま1行ずつ書いた場合と、
// (2) 改行コードで区切り直した場合の2通りで表示する。

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SimpleSerialTester.Core;

Console.OutputEncoding = Encoding.UTF8;

var port = Option("--port", PortSettings.LoopbackName);
var baud = int.Parse(Option("--baud", "9600"));
var lines = int.Parse(Option("--lines", "5"));
var length = int.Parse(Option("--length", "40"));
var gap = int.Parse(Option("--gap", "30"));

var settings = new PortSettings(port, baud, 8, ParityKind.None, StopBitsKind.One);
using ISerialTransport transport = settings.IsLoopback ? new LoopbackTransport() : new SerialPortTransport(settings);

var watch = new Stopwatch();
var chunks = new ConcurrentQueue<(double Ms, byte[] Data)>();
transport.DataReceived += (data, _) => chunks.Enqueue((watch.Elapsed.TotalMilliseconds, data));
transport.Faulted += message => Console.Error.WriteLine($"受信の異常: {message}");

transport.Open();
watch.Start();

var sentBytes = 0;
for (var i = 1; i <= lines; i++)
{
    var head = $"LINE-{i:000} ";
    var body = head + new string((char)('A' + (i - 1) % 26), Math.Max(0, length - head.Length));
    var payload = Encoding.ASCII.GetBytes(body + "\r\n");
    transport.Write(payload);
    sentBytes += payload.Length;
    await Task.Delay(gap);
}

// 送った分がすべて返るか、3秒たつまで待つ。
var deadline = watch.Elapsed + TimeSpan.FromSeconds(3);
while (chunks.Sum(chunk => chunk.Data.Length) < sentBytes && watch.Elapsed < deadline)
{
    await Task.Delay(5);
}

transport.Close();
var received = chunks.ToArray();

Console.WriteLine($"接続先: {(settings.IsLoopback ? "ループバック" : $"{settings.PortName} {settings.Summary}")}");
Console.WriteLine($"送信: {lines} 行、{sentBytes} バイト（1行 {length + 2} バイト、{gap} ミリ秒おき）");
Console.WriteLine();

Console.WriteLine("(1) 通知が来た単位のまま、1行ずつ書いた場合");
foreach (var (ms, data) in received)
{
    Console.WriteLine($"  {ms,7:F1} ms  {data.Length,3} バイト  {TextCodec.ToDisplay(data)}");
}

Console.WriteLine();
Console.WriteLine("(2) 改行コード（CR+LF）で区切り直した場合");
var assembler = new FrameAssembler { Terminator = LineEnding.CRLF };
var frames = new List<(double Ms, Frame Frame)>();
var origin = DateTime.Today;
foreach (var (ms, data) in received)
{
    foreach (var frame in assembler.Push(data, origin.AddMilliseconds(ms)))
    {
        frames.Add(((frame.StartedAt - origin).TotalMilliseconds, frame));
    }
}

if (assembler.FlushAll() is { } rest)
{
    frames.Add(((rest.StartedAt - origin).TotalMilliseconds, rest));
}

foreach (var (ms, frame) in frames)
{
    Console.WriteLine($"  {ms,7:F1} ms  {frame.Data.Length,3} バイト  {TextCodec.ToDisplay(frame.Data)}{(frame.Terminated ? "" : "  （区切りなし）")}");
}

var endingAtBoundary = received.Count(chunk => chunk.Data.Length >= 2 && chunk.Data[^2] == 0x0D && chunk.Data[^1] == 0x0A);
Console.WriteLine();
Console.WriteLine($"通知の回数: {received.Length}（うち、行の終わりでちょうど切れていたもの: {endingAtBoundary}）");
Console.WriteLine($"区切り直した件数: {frames.Count}（送った行数: {lines}）");
Console.WriteLine($"受信したバイト数: {received.Sum(chunk => chunk.Data.Length)} / {sentBytes}");

string Option(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}
