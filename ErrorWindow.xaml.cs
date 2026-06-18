using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Snipper;

/// <summary>
/// A copyable error dialog. Unlike a MessageBox, the body text is selectable,
/// there's a one-click Copy button, and the full (untruncated) log can be saved
/// to a file. Use for anything that surfaces an ffmpeg log.
/// </summary>
public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Show a copyable error. <paramref name="heading"/> is a short
    /// human summary; <paramref name="detail"/> is the full log / technical text.</summary>
    public static void Show(Window? owner, string heading, string detail)
    {
        var w = new ErrorWindow();
        if (owner is { IsLoaded: true })
            w.Owner = owner;
        else
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.Heading.Text = heading;
        w.Body.Text = detail;
        w.ShowDialog();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Body.Text); CopyBtn.Content = "Copied ✓"; } catch { }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save log",
            FileName = "snipper-error.log",
            Filter = "Log file|*.log|Text file|*.txt|All files|*.*",
        };
        if (dlg.ShowDialog(this) == true)
        {
            try { File.WriteAllText(dlg.FileName, Body.Text); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save:\n" + ex.Message, "Snipper");
            }
        }
    }
}
