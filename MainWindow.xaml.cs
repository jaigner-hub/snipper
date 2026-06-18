using System.Windows;

namespace Snipper;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ShowFfmpegStatus();
    }

    private void ShowFfmpegStatus()
    {
        if (Ffmpeg.IsAvailable)
        {
            FfmpegStatus.Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#FF6FE0A0")!;
            FfmpegStatus.Text = $"ffmpeg: {Ffmpeg.Path}";
        }
        else
        {
            FfmpegStatus.Foreground = (System.Windows.Media.Brush)
                new System.Windows.Media.BrushConverter().ConvertFrom("#FFE08A6F")!;
            FfmpegStatus.Text = "ffmpeg NOT found. Put ffmpeg.exe next to Snipper.exe, " +
                                "in a ./ffmpeg/ folder, or on your PATH.";
            NewSnipBtn.IsEnabled = false;
        }
    }

    private SnipSettings ReadSettings()
    {
        int fps = FpsBox.SelectedIndex switch { 0 => 12, 2 => 30, _ => 20 };
        int.TryParse(MaxSecBox.Text, out int maxSec);
        return new SnipSettings
        {
            Fps = fps,
            CaptureCursor = CursorBox.IsChecked == true,
            MaxSeconds = Math.Max(0, maxSec),
        };
    }

    private async void NewSnip_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();

        // Get out of the way so we don't capture our own window.
        Hide();
        await Task.Delay(180);

        // 1) Pick a region.
        var overlay = new RegionOverlayWindow();
        bool? picked = overlay.ShowDialog();
        if (picked != true || overlay.Result is not CaptureRect rect)
        {
            Show();
            return;
        }

        // 2) Optional countdown so you can un-pause the video before capture.
        int countdown = CountdownBox.SelectedIndex switch { 1 => 3, 2 => 5, _ => 0 };
        if (countdown > 0)
        {
            var cd = new CountdownWindow(countdown);
            cd.ShowDialog();
        }

        // 3) Record it. Recording starts immediately; the bar controls stop/cancel.
        var recorder = new Recorder();
        try
        {
            recorder.Start(rect, settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't start recording:\n{ex.Message}", "Snipper",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Show();
            return;
        }

        var bar = new RecordBarWindow();
        bool? kept = bar.ShowDialog();

        if (kept != true)
        {
            recorder.Cancel();
            Show();
            return;
        }

        string clip = await recorder.StopAsync();

        // A successful gdigrab capture is at least a few KB. A tiny/missing file
        // means ffmpeg failed to capture (bad region, DRM/HW-overlay content,
        // off-desktop offset, etc.) — show its log instead of a black 0s clip.
        long size = 0;
        try { if (System.IO.File.Exists(clip)) size = new System.IO.FileInfo(clip).Length; } catch { }
        if (size < 10_000)
        {
            ErrorWindow.Show(this,
                "Recording produced an empty clip (ffmpeg captured no frames). " +
                "Common causes: the region is off-screen, your display layout/DPI " +
                "changed, or the content is DRM/hardware-overlay (e.g. a fullscreen " +
                "game or a protected video) which gdigrab can't read.",
                recorder.Log);
            return;
        }

        // 4) Edit + export.
        var editor = new EditorWindow(clip, settings.Fps);
        editor.Owner = this;
        Show();
        editor.Show();
    }
}
