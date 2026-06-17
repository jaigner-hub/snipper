using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Snipper;

/// <summary>
/// Puts a GIF on the clipboard in several formats so it pastes well everywhere:
///   • FileDrop  — Ctrl+V into Discord/Slack/Teams/Explorer uploads the animated file
///   • "GIF" / "image/gif" — raw bytes for web apps / browsers
///   • Bitmap    — first frame, so image editors that ignore the above still paste something
/// Must be called on the STA UI thread (it is, from a WPF click handler).
/// </summary>
public static class ClipboardHelper
{
    public static void CopyGif(string gifPath)
    {
        var data = new DataObject();

        // 1) File drop — the broadly useful one (keeps animation in chat apps).
        var files = new StringCollection { gifPath };
        data.SetFileDropList(files);

        // 2) Raw GIF bytes under common format names.
        byte[] bytes = File.ReadAllBytes(gifPath);
        var ms = new MemoryStream(bytes);
        data.SetData("GIF", ms);
        data.SetData("image/gif", ms);

        // 3) First frame as a static bitmap fallback.
        try
        {
            var decoder = new GifBitmapDecoder(
                new Uri(gifPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0)
                data.SetImage(decoder.Frames[0]);
        }
        catch { /* fallback only — fine to skip */ }

        // copy=true so it survives after the app closes.
        Clipboard.SetDataObject(data, true);
    }
}
