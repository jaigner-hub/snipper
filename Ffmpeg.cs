using System.Diagnostics;
using System.IO;

namespace Snipper;

/// <summary>
/// Locates ffmpeg.exe and runs it. Capture + all encoding goes through here.
///
/// Resolution order:
///   1. ./ffmpeg.exe next to Snipper.exe
///   2. ./ffmpeg/ffmpeg.exe (the folder the .csproj copies a bundled build into)
///   3. ffmpeg on PATH
/// </summary>
public static class Ffmpeg
{
    private static string? _cached;

    /// <summary>Full path to ffmpeg.exe, or null if it can't be found.</summary>
    public static string? Path
    {
        get
        {
            if (_cached != null) return _cached;

            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                System.IO.Path.Combine(baseDir, "ffmpeg.exe"),
                System.IO.Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) { _cached = c; return _cached; }

            // Fall back to PATH.
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var p = System.IO.Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(p)) { _cached = p; return _cached; }
                }
                catch { /* malformed PATH entry */ }
            }
            return null;
        }
    }

    public static bool IsAvailable => Path != null;

    /// <summary>
    /// Starts ffmpeg with the given argument string. The caller owns the Process
    /// (used for recording, where we need to send 'q' to stop gracefully).
    /// stdin is kept open so we can write 'q'.
    /// </summary>
    public static Process Start(string args, DataReceivedEventHandler? onStderr = null)
    {
        var exe = Path ?? throw new FileNotFoundException(
            "ffmpeg.exe not found. Put it next to Snipper.exe, in ./ffmpeg/, or on PATH.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (onStderr != null) proc.ErrorDataReceived += onStderr;
        proc.Start();
        if (onStderr != null) proc.BeginErrorReadLine();
        return proc;
    }

    /// <summary>
    /// Runs ffmpeg to completion (for one-shot encodes/exports). Returns the
    /// exit code and the captured stderr (ffmpeg logs progress to stderr).
    /// </summary>
    public static async Task<(int exitCode, string log)> RunAsync(
        string args, CancellationToken ct = default, string? workingDir = null)
    {
        var exe = Path ?? throw new FileNotFoundException(
            "ffmpeg.exe not found. Put it next to Snipper.exe, in ./ffmpeg/, or on PATH.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDir ?? "",
        };
        using var proc = new Process { StartInfo = psi };
        var log = new System.Text.StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, _) => { };
        proc.Start();
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        await using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } }))
        {
            await proc.WaitForExitAsync(ct);
        }
        return (proc.ExitCode, log.ToString());
    }
}
