using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Snipper;

/// <summary>
/// Tiny always-on-top strip shown while recording: elapsed time + Stop/Cancel.
/// DialogResult true = stop &amp; keep, false = cancel &amp; discard.
/// </summary>
public partial class RecordBarWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private bool _blinkOn = true;

    public RecordBarWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PlaceBottomCenter();
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? s, EventArgs e)
    {
        ElapsedText.Text = $"REC  {_sw.Elapsed.TotalSeconds:0.0}s";
        // Blink the dot roughly twice a second.
        _blinkOn = !_blinkOn;
        RecDot.Opacity = _blinkOn ? 1.0 : 0.25;
    }

    private void PlaceBottomCenter()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Bottom - ActualHeight - 24;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = false;
        Close();
    }
}
