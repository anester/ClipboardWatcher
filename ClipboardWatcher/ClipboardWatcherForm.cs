using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipboardWatcher;

public sealed class ClipboardWatcherForm : Form
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    public event EventHandler<ClipboardSnapshot>? ClipboardChanged;

    public ClipboardWatcherForm()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Width = 0;
        Height = 0;

        // Force handle creation so we can subscribe to clipboard notifications even though the form stays hidden.
        var _ = Handle;
        NativeMethods.AddClipboardFormatListener(Handle);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void SetVisibleCore(bool value)
    {
        // Keep the form hidden while allowing it to receive window messages.
        base.SetVisibleCore(false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE)
        {
            HandleClipboardChange();
        }

        base.WndProc(ref m);
    }

    private void HandleClipboardChange()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                ClipboardChanged?.Invoke(this, ClipboardSnapshot.FromText(text));
            }
            else if (Clipboard.ContainsImage())
            {
                using var image = Clipboard.GetImage();
                if (image is not null)
                {
                    ClipboardChanged?.Invoke(this, ClipboardSnapshot.FromImage(image));
                }
            }
        }
        catch (ExternalException)
        {
            // Clipboard is busy; ignore this cycle.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard handling failed: {ex}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
            }
            catch
            {
                // no-op
            }
        }

        base.Dispose(disposing);
    }
}

public sealed record ClipboardSnapshot(string? Text, byte[]? ImageBytes)
{
    public static ClipboardSnapshot FromText(string text) => new(text, null);

    public static ClipboardSnapshot FromImage(Image image)
    {
        using var ms = new System.IO.MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return new ClipboardSnapshot(null, ms.ToArray());
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
