using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snipper;

/// <summary>
/// Snipping-tool style overlay: a transparent full-virtual-desktop window with a
/// dark dim layer. Dragging punches a "hole" in the dim that reveals the live
/// desktop underneath, and on release we hand back the selection in PHYSICAL
/// pixels so ffmpeg gdigrab captures exactly that region.
/// </summary>
public partial class RegionOverlayWindow : Window
{
    private Point _start;
    private bool _dragging;
    private Rect _selDip;          // current selection in window DIPs

    /// <summary>Set on a successful selection (physical pixels). Null if cancelled.</summary>
    public CaptureRect? Result { get; private set; }

    // --- Win32: physical bounds of the whole virtual desktop ---------------
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    public RegionOverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        // Redraw the dim layer whenever the window is sized.
        SizeChanged += (_, _) => Redraw();
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        // Cover the entire virtual desktop, in DIPs (SystemParameters are DIPs).
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Redraw();
        // Center the hint near the top.
        Canvas.SetLeft(HintBadge, (Width / 2) - 150);
        Canvas.SetTop(HintBadge, 24);

        Activate();
        Focus();
    }

    private void OnKey(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Result = null; DialogResult = false; Close(); }
    }

    private void OnDown(object? s, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        _dragging = true;
        HintBadge.Visibility = Visibility.Collapsed;
        SelRect.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object? s, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Root);
        _selDip = new Rect(_start, p);

        Canvas.SetLeft(SelRect, _selDip.X);
        Canvas.SetTop(SelRect, _selDip.Y);
        SelRect.Width = _selDip.Width;
        SelRect.Height = _selDip.Height;

        var phys = ToPhysical(_selDip);
        SizeText.Text = $"{phys.Width} x {phys.Height}";
        Canvas.SetLeft(SizeBadge, _selDip.X);
        Canvas.SetTop(SizeBadge, Math.Max(0, _selDip.Y - 24));

        Redraw();
    }

    private void OnUp(object? s, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var phys = ToPhysical(_selDip).ToEven();
        if (!phys.IsValid)
        {
            // Too small — treat as a cancel rather than recording a 2px clip.
            Result = null;
            DialogResult = false;
        }
        else
        {
            Result = phys;
            DialogResult = true;
        }
        Close();
    }

    /// <summary>
    /// Map a selection given in window DIPs to physical desktop pixels.
    /// We map by fraction across the window onto the physical virtual-screen
    /// rect from GetSystemMetrics, which is correct for any uniform DPI scale.
    /// (Mixed per-monitor DPI is a known v1 limitation — see README.)
    /// </summary>
    private CaptureRect ToPhysical(Rect dip)
    {
        double px = GetSystemMetrics(SM_XVIRTUALSCREEN);
        double py = GetSystemMetrics(SM_YVIRTUALSCREEN);
        double pw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        double ph = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        double sx = pw / Width;   // physical px per DIP, X
        double sy = ph / Height;  // physical px per DIP, Y

        int x = (int)Math.Round(px + dip.X * sx);
        int y = (int)Math.Round(py + dip.Y * sy);
        int w = (int)Math.Round(dip.Width * sx);
        int h = (int)Math.Round(dip.Height * sy);
        return new CaptureRect(x, y, w, h);
    }

    /// <summary>Rebuild the dim layer as full-screen minus the selection hole.</summary>
    private void Redraw()
    {
        var outer = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (_selDip.Width > 0 && _selDip.Height > 0)
        {
            var hole = new RectangleGeometry(_selDip);
            DimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, hole);
        }
        else
        {
            DimPath.Data = outer;
        }
    }
}
