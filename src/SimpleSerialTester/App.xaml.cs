using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleSerialTester;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --screenshot <出力先.png> [--tab <0|1|2>]
        // 説明用の画面を作って画像に保存し、終了する（READMEの画像を作り直すため）。
        var index = Array.IndexOf(e.Args, "--screenshot");
        if (index >= 0 && index + 1 < e.Args.Length)
        {
            var tabAt = Array.IndexOf(e.Args, "--tab");
            var tab = tabAt >= 0 && tabAt + 1 < e.Args.Length && int.TryParse(e.Args[tabAt + 1], out var value) ? value : 0;
            RunScreenshot(e.Args[index + 1], tab);
            return;
        }

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimpleSerialTester",
            "settings.json");
        new MainWindow(settingsPath).Show();
    }

    private void RunScreenshot(string outputPath, int tabIndex)
    {
        // 設定ファイルを読み書きしない状態で開く。利用者の履歴や登録リストを画像へ写さないため。
        var window = new MainWindow(settingsPath: null);
        window.Loaded += async (_, _) =>
        {
            await window.RunDemoAsync(tabIndex);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var root = (FrameworkElement)window.Content;
            const double Scale = 2.0;
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth * Scale),
                (int)Math.Ceiling(root.ActualHeight * Scale),
                96 * Scale,
                96 * Scale,
                PixelFormats.Pbgra32);
            bitmap.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (var stream = File.Create(fullPath))
            {
                encoder.Save(stream);
            }

            Shutdown();
        };
        window.Show();
    }
}
