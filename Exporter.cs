using System.IO;
using System.Text;

namespace Snipper;

/// <summary>
/// Turns a recorded clip + edit settings into the final shareable file.
/// All the heavy lifting is one ffmpeg invocation per export.
/// </summary>
public static class Exporter
{
    public static string DefaultExtension(OutputFormat f) => f switch
    {
        OutputFormat.Gif => ".gif",
        OutputFormat.Mp4 => ".mp4",
        OutputFormat.Mov => ".mov",
        OutputFormat.WebP => ".webp",
        OutputFormat.Apng => ".png",
        _ => ".gif",
    };

    public static async Task<(bool ok, string log)> ExportAsync(
        ExportJob job, CancellationToken ct = default)
    {
        // Caption text goes into temp files so we never have to escape user text
        // into the filtergraph. Cleaned up after the encode.
        var tempFiles = new List<string>();
        try
        {
            string vf = BuildFilter(job, tempFiles);

            var sb = new StringBuilder("-hide_banner -y ");

            // Input-side trim is fast and accurate enough for short meme clips.
            if (job.TrimStart > 0)
                sb.Append($"-ss {job.TrimStart.ToString(System.Globalization.CultureInfo.InvariantCulture)} ");
            sb.Append($"-i \"{job.SourcePath}\" ");
            if (job.TrimEnd > job.TrimStart)
            {
                double dur = job.TrimEnd - job.TrimStart;
                sb.Append($"-t {dur.ToString(System.Globalization.CultureInfo.InvariantCulture)} ");
            }

            sb.Append($"-vf \"{vf}\" -an ");
            sb.Append(EncoderArgs(job.Format));
            sb.Append($"\"{job.OutputPath}\"");

            var (code, log) = await Ffmpeg.RunAsync(sb.ToString(), ct);
            return (code == 0 && File.Exists(job.OutputPath), log);
        }
        finally
        {
            foreach (var f in tempFiles) { try { File.Delete(f); } catch { } }
        }
    }

    private static string EncoderArgs(OutputFormat f) => f switch
    {
        // GIF encoding is folded into the filter (palette). Just the muxer here.
        OutputFormat.Gif => "",
        OutputFormat.Mp4 => "-c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p -movflags +faststart ",
        OutputFormat.Mov => "-c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p -movflags +faststart ",
        OutputFormat.WebP => "-c:v libwebp -lossless 0 -q:v 75 -loop 0 -preset picture ",
        OutputFormat.Apng => "-c:v apng -plays 0 -f apng ",
        _ => "",
    };

    private static string BuildFilter(ExportJob job, List<string> tempFiles)
    {
        var chain = new List<string> { $"fps={job.Fps}" };

        if (job.Width > 0)
            chain.Add($"scale={job.Width}:-2:flags=lanczos");

        for (int i = 0; i < job.Captions.Count; i++)
        {
            var c = job.Captions[i];
            if (string.IsNullOrWhiteSpace(c.Text)) continue;
            chain.Add(DrawText(c, i, tempFiles));
        }

        string body = string.Join(",", chain);

        // GIF: append the 2-pass palette graph so colors don't look like 1996.
        if (job.Format == OutputFormat.Gif)
        {
            body += ",split[s0][s1];[s0]palettegen=stats_mode=diff[p];" +
                    "[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";
        }
        return body;
    }

    private static string DrawText(Caption c, int index, List<string> tempFiles)
    {
        // Write the caption to a temp UTF-8 file; drawtext reads it via textfile=.
        string path = Path.Combine(Path.GetTempPath(), "Snipper", $"cap_{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Meme captions are conventionally uppercase.
        File.WriteAllText(path, c.Text.ToUpperInvariant(), new UTF8Encoding(false));
        tempFiles.Add(path);

        string tf = EscapeFilterPath(path);
        string font = ResolveFontEscaped();

        // Top anchors from the top edge; bottom anchors up from the bottom edge.
        string y = c.IsBottom
            ? $"h-text_h-(h*{Frac(c.VPosition)})"
            : $"(h*{Frac(c.VPosition)})";

        int border = Math.Max(2, c.FontSize / 16);

        // No surrounding single quotes: on Windows the escaped drive colon (C\:)
        // inside single quotes gets mangled. Unquoted forward-slash paths with
        // just the colon escaped is the form that actually works. Spaces in the
        // path are fine — the filtergraph only splits on : , ; [ ] and the whole
        // -vf value is already double-quoted at the process-arg level.
        return
            $"drawtext=fontfile={font}:textfile={tf}:expansion=none:" +
            $"fontcolor=white:fontsize={c.FontSize}:" +
            $"borderw={border}:bordercolor=black:" +
            $"x=(w-text_w)/2:y={y}";
    }

    private static string Frac(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Impact = the meme font. Falls back to Arial Bold if absent.</summary>
    private static string ResolveFontEscaped()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string impact = Path.Combine(win, "Fonts", "impact.ttf");
        string arial = Path.Combine(win, "Fonts", "arialbd.ttf");
        string chosen = File.Exists(impact) ? impact : arial;
        return EscapeFilterPath(chosen);
    }

    /// <summary>
    /// ffmpeg filtergraphs treat ':' as an option separator and '\' specially,
    /// so a Windows path needs forward slashes and an escaped drive colon.
    /// </summary>
    private static string EscapeFilterPath(string p)
        => p.Replace('\\', '/').Replace(":", "\\:");
}
