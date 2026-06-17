using System.Diagnostics;
using System.IO;

namespace Snipper;

/// <summary>
/// Records a screen region with ffmpeg's gdigrab into a temp mp4. The intermediate
/// is encoded fast + near-lossless so the editor/exporter has clean source to
/// trim, caption and re-encode from.
/// </summary>
public sealed class Recorder
{
    private Process? _proc;
    public string OutputPath { get; }
    public bool IsRecording => _proc is { HasExited: false };

    public Recorder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Snipper");
        Directory.CreateDirectory(dir);
        // Note: no Date.now in this env at author time — the app picks a name at runtime.
        OutputPath = Path.Combine(dir, $"capture_{Guid.NewGuid():N}.mp4");
    }

    public void Start(CaptureRect rect, SnipSettings settings)
    {
        rect = rect.ToEven();
        if (!rect.IsValid) throw new ArgumentException("Capture region too small.");

        int drawMouse = settings.CaptureCursor ? 1 : 0;
        string timeCap = settings.MaxSeconds > 0 ? $"-t {settings.MaxSeconds} " : "";

        // gdigrab grabs the desktop; offset/video_size carve out our region.
        // ultrafast + crf 18 keeps the encoder fast enough to capture in realtime.
        string args =
            $"-hide_banner -f gdigrab -framerate {settings.Fps} " +
            $"-draw_mouse {drawMouse} " +
            $"-offset_x {rect.X} -offset_y {rect.Y} " +
            $"-video_size {rect.Width}x{rect.Height} " +
            $"-i desktop " +
            timeCap +
            $"-c:v libx264 -preset ultrafast -crf 18 -pix_fmt yuv420p " +
            $"-movflags +faststart -y \"{OutputPath}\"";

        _proc = Ffmpeg.Start(args);
    }

    /// <summary>
    /// Stops recording gracefully (sends 'q' so ffmpeg finalizes the moov atom)
    /// and waits for the file to be written. Returns the path to the clip.
    /// </summary>
    public async Task<string> StopAsync()
    {
        if (_proc == null) return OutputPath;
        try
        {
            if (!_proc.HasExited)
            {
                await _proc.StandardInput.WriteAsync('q');
                await _proc.StandardInput.FlushAsync();
            }
            // Give ffmpeg a moment to finalize; force-kill if it hangs.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { _proc.Kill(true); } catch { } }
        }
        catch { /* process already gone */ }
        return OutputPath;
    }

    public void Cancel()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        try { if (File.Exists(OutputPath)) File.Delete(OutputPath); } catch { }
    }
}
