using System.Diagnostics;
using System.IO;
using System.Windows;
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

        _in = 0;
        _out = _duration;
        PosSlider.Maximum = _duration;
        UpdateTrimLabels();
        UpdateTimeLabel(0);
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
    }

    private void SetOut_Click(object sender, RoutedEventArgs e)
    {
        _out = Math.Max(PosSlider.Value, _in + 0.05);
        if (_out > _duration) _out = _duration;
        UpdateTrimLabels();
    }

    private void ResetTrim_Click(object sender, RoutedEventArgs e)
    {
        _in = 0; _out = _duration;
        UpdateTrimLabels();
    }

    private void UpdateTrimLabels()
    {
        InText.Text = $"in {_in:0.0}s";
        OutText.Text = $"out {_out:0.0}s  ({_out - _in:0.0}s clip)";
    }

    private void UpdateTimeLabel(double pos) =>
        TimeText.Text = $"{pos:0.0} / {_duration:0.0}s";

    // ---- captions (live preview) ------------------------------------------

    private void Caption_Changed(object sender, RoutedEventArgs e)
    {
        if (TopPreview == null) return; // during init
        TopPreview.Text = (TopBox.Text ?? "").ToUpperInvariant();
        BottomPreview.Text = (BottomBox.Text ?? "").ToUpperInvariant();
        // Scale preview font relative to a nominal 480px-wide design.
        double fs = FontSlider.Value;
        TopPreview.FontSize = fs * 0.85;
        BottomPreview.FontSize = fs * 0.85;
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
            string tail = log.Length > 1500 ? log[^1500..] : log;
            MessageBox.Show(this, "ffmpeg failed:\n\n" + tail, "Snipper — export error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool busy, string? status)
    {
        ExportBtn.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = busy;
        if (status != null) StatusText.Text = status;
    }
}
