using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Snipper;

/// <summary>
/// Full-screen click-through countdown. It floats "3 · 2 · 1" over everything but
/// lets mouse clicks pass through to the window behind, so you can press Play on
/// your video while it counts down. Closes itself when it hits zero.
/// </summary>
public partial class CountdownWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining;

    // Make the window click-through so clicks reach the video underneath.
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;

    public CountdownWindow(int seconds)
    {
        InitializeComponent();
        _remaining = Math.Max(1, seconds);
        Number.Text = _remaining.ToString();
        _timer.Tick += OnTick;
        Loaded += (_, _) => _timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    private void OnTick(object? s, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            _timer.Stop();
            DialogResult = true;
            Close();
        }
        else
        {
            Number.Text = _remaining.ToString();
        }
    }
}
