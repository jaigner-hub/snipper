using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Snipper;

public partial class EditorWindow : Window
{
    private readonly string _sourcePath;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _duration;          // seconds
    private double _in;
    private double _out;
    private bool _playing;
    private bool _suppressSlider;      // guards programmatic slider updates
    private int _srcWidth;             // native clip dimensions, for caption fit
    private int _srcHeight;

    public EditorWindow(string sourcePath, int captureFps)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        FpsBox.Text = captureFps.ToString();

        _timer.Tick += OnTick;
        Loaded += (_, _) =>
        {
            Player.Source = new Uri(_sourcePath);
            // Nudge the pipeline so the first frame renders without pressing play.
            Player.Play();
            Player.Pause();
        };
        Closed += (_, _) => { _timer.Stop(); try { Player.Close(); } catch { } };
    }

    // ---- media lifecycle ---------------------------------------------------

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        _duration = Player.NaturalDuration.HasTimeSpan
            ? Player.NaturalDuration.TimeSpan.TotalSeconds
            : 0;
        if (_duration <= 0) _duration = 0.1;

        _srcWidth = Player.NaturalVideoWidth;
        _srcHeight = Player.NaturalVideoHeight;
        UpdatePreview();

        _in = 0;
        _out = _duration;
        PosSlider.Maximum = _duration;
        UpdateTrimLabels();
        UpdateTimeLabel(0);
        LayoutTrim();
        UpdatePlayhead(0);
        _timer.Start();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) => SeekTo(_in);

    // ---- playback ----------------------------------------------------------

    private void OnTick(object? s, EventArgs e)
    {
        if (!_playing) return;
        double pos = Player.Position.TotalSeconds;

        // Loop within the trimmed range.
        if (pos >= _out || pos < _in)
        {
            SeekTo(_in);
            pos = _in;
        }

        _suppressSlider = true;
        PosSlider.Value = pos;
        _suppressSlider = false;
        UpdateTimeLabel(pos);
        UpdatePlayhead(pos);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_playing) { Player.Pause(); _playing = false; PlayBtn.Content = "▶"; }
        else
        {
            if (Player.Position.TotalSeconds < _in || Player.Position.TotalSeconds >= _out)
                SeekTo(_in);
            Player.Play(); _playing = true; PlayBtn.Content = "❚❚";
        }
    }

    private void PosSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSlider) return;       // came from the timer, not the user
        SeekTo(e.NewValue);
        UpdateTimeLabel(e.NewValue);
        UpdatePlayhead(e.NewValue);
    }

    private void SeekTo(double seconds)
    {
        Player.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _duration));
    }

    // ---- trim --------------------------------------------------------------

    private void SetIn_Click(object sender, RoutedEventArgs e)
    {
        _in = Math.Min(PosSlider.Value, _out - 0.05);
        if (_in < 0) _in = 0;
        UpdateTrimLabels();
        LayoutTrim();
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        _out = Math.Max(PosSlider.Value, _in + 0.05);
        if (_out > _duration) _out = _duration;
        UpdateTrimLabels();
        LayoutTrim();
    }

    private void ResetTrim_Click(object sender, RoutedEventArgs e)
    {
        _in = 0; _out = _duration;
        UpdateTrimLabels();
        LayoutTrim();
    }

    private void UpdateTrimLabels()
    {
        InText.Text = $"in {_in:0.0}s";
        OutText.Text = $"out {_out:0.0}s  ({_out - _in:0.0}s clip)";
    }

    // ---- visual trim bar (drag handles) -----------------------------------

    private const double HandleW = 12;

    private void TrimCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutTrim();

    private void LeftHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double w = TrimCanvas.ActualWidth;
        if (w <= 0 || _duration <= 0) return;
        double dt = e.HorizontalChange / w * _duration;
        _in = Math.Clamp(_in + dt, 0, _out - 0.05);
        UpdateTrimLabels();
        LayoutTrim();
        SeekTo(_in);                 // jump preview to the new front edge
        UpdatePlayhead(_in);
        if (_playing) { Player.Pause(); _playing = false; PlayBtn.Content = "▶"; }
    }

    private void RightHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double w = TrimCanvas.ActualWidth;
        if (w <= 0 || _duration <= 0) return;
        double dt = e.HorizontalChange / w * _duration;
        _out = Math.Clamp(_out + dt, _in + 0.05, _duration);
        UpdateTrimLabels();
        LayoutTrim();
        SeekTo(_out);                // jump preview to the new back edge
        UpdatePlayhead(_out);
        if (_playing) { Player.Pause(); _playing = false; PlayBtn.Content = "▶"; }
    }

    private void LayoutTrim()
    {
        double w = TrimCanvas.ActualWidth;
        if (w <= 0 || _duration <= 0) return;

        TrimTrack.Width = w;

        double inX = _in / _duration * w;
        double outX = _out / _duration * w;

        Canvas.SetLeft(TrimSel, inX);
        TrimSel.Width = Math.Max(0, outX - inX);

        Canvas.SetLeft(LeftHandle, Math.Clamp(inX - HandleW / 2, 0, w - HandleW));
        Canvas.SetLeft(RightHandle, Math.Clamp(outX - HandleW / 2, 0, w - HandleW));
    }

    private void UpdatePlayhead(double pos)
    {
        double w = TrimCanvas.ActualWidth;
        if (w <= 0 || _duration <= 0) return;
        Canvas.SetLeft(Playhead, Math.Clamp(pos / _duration * w, 0, w - 2));
    }

    private void UpdateTimeLabel(double pos) =>
        TimeText.Text = $"{pos:0.0} / {_duration:0.0}s";

    // ---- captions (live preview) ------------------------------------------

    private void Caption_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void PreviewArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreview();

    /// <summary>
    /// Lay out the caption previews to match what the export will actually produce:
    /// the same auto-fit font size and frame width, positioned within the
    /// letterboxed video rectangle (the MediaElement uses Stretch=Uniform).
    /// </summary>
    private void UpdatePreview()
    {
        if (TopPreview == null) return; // during init
        TopPreview.Text = (TopBox.Text ?? "").ToUpperInvariant();
        BottomPreview.Text = (BottomBox.Text ?? "").ToUpperInvariant();

        if (_srcWidth <= 0 || _srcHeight <= 0) return;

        int.TryParse(WidthBox.Text, out int width);
        int renderWidth = width > 0 ? width : _srcWidth;
        int renderHeight = width > 0
            ? (int)Math.Round((double)_srcHeight * renderWidth / _srcWidth)
            : _srcHeight;

        double cellW = PreviewArea.ActualWidth, cellH = PreviewArea.ActualHeight;
        if (cellW <= 0 || cellH <= 0) return;

        // Uniform-fit the frame into the preview cell → the displayed video rect.
        double scale = Math.Min(cellW / renderWidth, cellH / renderHeight);
        double dispW = renderWidth * scale, dispH = renderHeight * scale;
        double barY = (cellH - dispH) / 2;   // letterbox bar above/below the video

        int slider = (int)FontSlider.Value;
        ApplyPreviewCaption(TopPreview, false, renderWidth, renderHeight, dispW, dispH, barY, scale, slider);
        ApplyPreviewCaption(BottomPreview, true, renderWidth, renderHeight, dispW, dispH, barY, scale, slider);
    }

    private static void ApplyPreviewCaption(System.Windows.Controls.TextBlock tb, bool bottom,
        int renderWidth, int renderHeight, double dispW, double dispH, double barY,
        double scale, int slider)
    {
        if (string.IsNullOrWhiteSpace(tb.Text)) return;
        int fs = Exporter.FitFontSize(tb.Text, renderWidth, renderHeight, slider);
        tb.FontSize = fs * scale;          // frame px → preview px
        tb.MaxWidth = dispW;               // wrap to the same frame width
        double margin = barY + dispH * 0.05;
        tb.Margin = bottom ? new Thickness(0, 0, 0, margin) : new Thickness(0, margin, 0, 0);
    }

    // ---- export ------------------------------------------------------------

    private OutputFormat SelectedFormat() => FormatBox.SelectedIndex switch
    {
        1 => OutputFormat.Mp4,
        2 => OutputFormat.Mov,
        3 => OutputFormat.WebP,
        4 => OutputFormat.Apng,
        _ => OutputFormat.Gif,
    };

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var fmt = SelectedFormat();
        int.TryParse(FpsBox.Text, out int fps);
        if (fps <= 0) fps = 20;
        int.TryParse(WidthBox.Text, out int width);

        var dlg = new SaveFileDialog
        {
            Title = "Save snip",
            FileName = "snip" + Exporter.DefaultExtension(fmt),
            Filter = fmt switch
            {
                OutputFormat.Gif => "GIF|*.gif",
                OutputFormat.Mp4 => "MP4|*.mp4",
                OutputFormat.Mov => "MOV|*.mov",
                OutputFormat.WebP => "WebP|*.webp",
                OutputFormat.Apng => "Animated PNG|*.png",
                _ => "All files|*.*",
            },
        };
        if (dlg.ShowDialog(this) != true) return;

        var job = new ExportJob
        {
            SourcePath = _sourcePath,
            OutputPath = dlg.FileName,
            Format = fmt,
            TrimStart = _in,
            TrimEnd = _out,
            Fps = fps,
            Width = Math.Max(0, width),
            SourceWidth = _srcWidth,
            SourceHeight = _srcHeight,
            Captions = new()
            {
                new Caption { Text = TopBox.Text ?? "", IsBottom = false,
                    VPosition = 0.05, FontSize = (int)FontSlider.Value },
                new Caption { Text = BottomBox.Text ?? "", IsBottom = true,
                    VPosition = 0.05, FontSize = (int)FontSlider.Value },
            },
        };

        SetBusy(true, "Exporting…");
        var (ok, log) = await Exporter.ExportAsync(job);
        SetBusy(false, null);

        if (ok)
        {
            StatusText.Text = $"✓ Saved: {Path.GetFileName(job.OutputPath)}";
            if (MessageBox.Show(this, "Export complete. Open the folder?", "Snipper",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start("explorer.exe", $"/select,\"{job.OutputPath}\"");
            }
        }
        else
        {
            StatusText.Text = "✗ Export failed. See details.";
            ErrorWindow.Show(this, "Export failed — ffmpeg returned an error.", log);
        }
    }

    private async void CopyGif_Click(object sender, RoutedEventArgs e)
    {
        int.TryParse(FpsBox.Text, out int fps);
        if (fps <= 0) fps = 20;
        int.TryParse(WidthBox.Text, out int width);

        // Always a GIF for the clipboard, to a temp file we leave in place
        // (the clipboard file-drop references the path after we return).
        string dir = Path.Combine(Path.GetTempPath(), "Snipper");
        Directory.CreateDirectory(dir);
        string outPath = Path.Combine(dir, $"clip_{Guid.NewGuid():N}.gif");

        var job = new ExportJob
        {
            SourcePath = _sourcePath,
            OutputPath = outPath,
            Format = OutputFormat.Gif,
            TrimStart = _in,
            TrimEnd = _out,
            Fps = fps,
            Width = Math.Max(0, width),
            SourceWidth = _srcWidth,
            SourceHeight = _srcHeight,
            Captions = new()
            {
                new Caption { Text = TopBox.Text ?? "", IsBottom = false,
                    VPosition = 0.05, FontSize = (int)FontSlider.Value },
                new Caption { Text = BottomBox.Text ?? "", IsBottom = true,
                    VPosition = 0.05, FontSize = (int)FontSlider.Value },
            },
        };

        SetBusy(true, "Building GIF for clipboard…");
        var (ok, log) = await Exporter.ExportAsync(job);

        if (ok)
        {
            try
            {
                ClipboardHelper.CopyGif(outPath);
                SetBusy(false, "✓ GIF copied — paste into Discord/chat with Ctrl+V");
            }
            catch (Exception ex)
            {
                SetBusy(false, "✗ Couldn't copy to clipboard.");
                ErrorWindow.Show(this, "Clipboard copy failed.", ex.ToString());
            }
        }
        else
        {
            SetBusy(false, "✗ GIF build failed. See details.");
            ErrorWindow.Show(this, "GIF build failed — ffmpeg returned an error.", log);
        }
    }

    private void SetBusy(bool busy, string? status)
    {
        ExportBtn.IsEnabled = !busy;
        CopyGifBtn.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = busy;
        if (status != null) StatusText.Text = status;
    }
}
