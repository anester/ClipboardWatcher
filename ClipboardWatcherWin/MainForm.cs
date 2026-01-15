using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace ClipboardWatcherWin;

public sealed class MainForm : Form
{
    private const string ClipboardUiFolderName = "clipboardui";
    private readonly WebView2 _webView;
    private LocalStaticFileServer? _server;

    public MainForm()
    {
        Text = "ClipboardWatcherWin";
        Width = 1200;
        Height = 800;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(_webView);
        Shown += async (_, _) => await InitializeAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _server?.Dispose();
        base.OnFormClosed(e);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var appRoot = GetClipboardUiRoot();
            if (!Directory.Exists(appRoot))
            {
                throw new DirectoryNotFoundException("Clipboard UI assets are missing. Build ClipboardWatcherWin to package them.");
            }

            _server ??= new LocalStaticFileServer(appRoot);
            var baseUrl = await _server.StartAsync();

            await _webView.EnsureCoreWebView2Async();
            _webView.Source = new Uri(baseUrl);
        }
        catch (Exception ex)
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.NavigateToString($"<pre>Failed to load Clipboard UI: {WebUtility.HtmlEncode(ex.Message)}</pre>");
        }
    }

    private static string GetClipboardUiRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, ClipboardUiFolderName);
    }
}
