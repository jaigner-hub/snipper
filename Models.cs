namespace Snipper;

/// <summary>A capture region in PHYSICAL pixels (what gdigrab wants).</summary>
public readonly record struct CaptureRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width >= 8 && Height >= 8;

    /// <summary>libx264 / yuv420p need even dimensions. Round down to even.</summary>
    public CaptureRect ToEven() => this with
    {
        Width = Width - (Width % 2),
        Height = Height - (Height % 2),
    };
}

/// <summary>Capture settings chosen on the control panel.</summary>
public sealed class SnipSettings
{
    public int Fps { get; set; } = 20;
    public bool CaptureCursor { get; set; } = false;
    /// <summary>Optional hard cap so a forgotten recording can't fill the disk (seconds, 0 = no cap).</summary>
    public int MaxSeconds { get; set; } = 60;
}

public enum OutputFormat { Gif, Mp4, Mov, WebP, Apng }

/// <summary>A burned-in meme caption.</summary>
public sealed class Caption
{
    public string Text { get; set; } = "";
    /// <summary>Vertical anchor as a fraction of height (0 = top, 1 = bottom).</summary>
    public double VPosition { get; set; } = 0.06;
    public int FontSize { get; set; } = 48;
    public bool IsBottom { get; set; }
}

/// <summary>Everything the exporter needs.</summary>
public sealed class ExportJob
{
    public string SourcePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public OutputFormat Format { get; set; } = OutputFormat.Gif;
    public double TrimStart { get; set; } = 0;     // seconds
    public double TrimEnd { get; set; } = 0;        // seconds (0 = to end)
    public int Fps { get; set; } = 20;
    /// <summary>Output width in px; height auto. 0 = keep source width.</summary>
    public int Width { get; set; } = 0;
    public List<Caption> Captions { get; set; } = new();
}
