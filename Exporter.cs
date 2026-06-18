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
        // We run ffmpeg with its working directory set to this folder so the font
        // and caption text files can be referenced by BARE FILENAME inside the
        // filtergraph. Windows drive-letter paths (C:\...) can't be reliably
        // escaped into an avfilter graph — the drive colon is the filter option
        // separator — so we sidestep the problem entirely. Caption text also goes
        // into files so we never escape user text into the graph either.
        string workDir = Path.Combine(Path.GetTempPath(), "Snipper");
        Directory.CreateDirectory(workDir);

        var tempFiles = new List<string>();
        try
        {
            string vf = BuildFilter(job, workDir, tempFiles);

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
            // Input/output are passed as ordinary (double-quoted) process args, not
            // inside the filtergraph, so their absolute paths are fine.
            sb.Append($"\"{job.OutputPath}\"");

            var (code, log) = await Ffmpeg.RunAsync(sb.ToString(), ct, workDir);
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

    private static string BuildFilter(ExportJob job, string workDir, List<string> tempFiles)
    {
        var chain = new List<string> { $"fps={job.Fps}" };

        if (job.Width > 0)
            chain.Add($"scale={job.Width}:-2:flags=lanczos");

        // Copy the meme font into the work dir once, so drawtext can reference it
        // by bare filename. Only bother if there's at least one caption to draw.
        string? fontFile = job.Captions.Any(c => !string.IsNullOrWhiteSpace(c.Text))
            ? CopyFontToWorkDir(workDir, tempFiles)
            : null;

        // The frame the captions are actually drawn onto (drawtext runs after the
        // scale filter): the output width if set, else the source width. Height
        // follows the same scale (scale uses -2 to preserve aspect).
        int renderWidth = job.Width > 0 ? job.Width : job.SourceWidth;
        int renderHeight = job.Width > 0 && job.SourceWidth > 0
            ? (int)Math.Round((double)job.SourceHeight * renderWidth / job.SourceWidth)
            : job.SourceHeight;

        foreach (var c in job.Captions)
        {
            if (string.IsNullOrWhiteSpace(c.Text)) continue;
            chain.Add(DrawText(c, renderWidth, renderHeight, workDir, fontFile!, tempFiles));
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

    private static string DrawText(Caption c, int renderWidth, int renderHeight,
        string workDir, string fontFile, List<string> tempFiles)
    {
        // Meme captions are conventionally uppercase. drawtext doesn't wrap or
        // auto-size, so a long caption would (a) run off both edges and (b) wrap
        // to more lines than fit the frame height. Shrink the font until the
        // wrapped block fits, then wrap at that size.
        string raw = c.Text.ToUpperInvariant();
        int fontSize = FitFontSize(raw, renderWidth, renderHeight, c.FontSize);
        string text = WrapText(raw, renderWidth, fontSize);

        // Write the caption to a UTF-8 file in the work dir; drawtext reads it via
        // textfile=. The filename is a bare name (no path) so the filtergraph
        // parser has no colon/backslash to misinterpret — ffmpeg resolves it
        // against its working directory (set in ExportAsync).
        string fileName = $"cap_{Guid.NewGuid():N}.txt";
        string path = Path.Combine(workDir, fileName);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        tempFiles.Add(path);

        // Top anchors from the top edge; bottom anchors up from the bottom edge.
        string y = c.IsBottom
            ? $"h-text_h-(h*{Frac(c.VPosition)})"
            : $"(h*{Frac(c.VPosition)})";

        int border = Math.Max(2, fontSize / 16);

        // fontfile + textfile are bare filenames resolved against ffmpeg's cwd.
        // text_align=C centers each wrapped line within the text block.
        return
            $"drawtext=fontfile={fontFile}:textfile={fileName}:expansion=none:" +
            $"fontcolor=white:fontsize={fontSize}:text_align=C:" +
            $"borderw={border}:bordercolor=black:" +
            $"x=(w-text_w)/2:y={y}";
    }

    /// <summary>
    /// Largest font size (≤ requested) at which the word-wrapped caption fits the
    /// frame: each caption gets up to ~45% of the height. Mirrors <see cref="WrapText"/>
    /// so the editor preview and the export agree. Returns the requested size if
    /// the frame dimensions are unknown.
    /// </summary>
    public static int FitFontSize(string text, int frameW, int frameH, int requested)
    {
        if (frameW <= 0 || frameH <= 0) return requested;

        double maxHeight = frameH * 0.45;
        for (int size = requested; size > 12; size -= 2)
        {
            int lines = WrapText(text, frameW, size).Count(ch => ch == '\n') + 1;
            if (lines * size * 1.25 <= maxHeight) return size;
        }
        return 12;
    }

    /// <summary>
    /// Greedy word-wrap so a caption fits the frame width. Impact is a condensed
    /// font; we estimate average glyph advance at ~0.5em and leave a small margin.
    /// If the width is unknown (0) the text is returned unwrapped.
    /// </summary>
    private static string WrapText(string text, int renderWidth, int fontSize)
    {
        if (renderWidth <= 0 || fontSize <= 0) return text;

        int maxChars = (int)(renderWidth * 0.94 / (fontSize * 0.5));
        if (maxChars < 1) maxChars = 1;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var w in words)
        {
            if (cur.Length == 0) cur.Append(w);
            else if (cur.Length + 1 + w.Length <= maxChars) cur.Append(' ').Append(w);
            else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(w); }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return string.Join("\n", lines);
    }

    private static string Frac(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Copies the meme font (Impact, or Arial Bold as a fallback) into the work
    /// dir and returns its bare filename, so drawtext can reference it without a
    /// Windows drive-letter path in the filtergraph. Added to tempFiles for cleanup.
    /// </summary>
    private static string CopyFontToWorkDir(string workDir, List<string> tempFiles)
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string impact = Path.Combine(win, "Fonts", "impact.ttf");
        string arial = Path.Combine(win, "Fonts", "arialbd.ttf");
        string src = File.Exists(impact) ? impact : arial;

        string destName = $"font_{Guid.NewGuid():N}{Path.GetExtension(src)}";
        string dest = Path.Combine(workDir, destName);
        File.Copy(src, dest, overwrite: true);
        tempFiles.Add(dest);
        return destName;
    }
}
