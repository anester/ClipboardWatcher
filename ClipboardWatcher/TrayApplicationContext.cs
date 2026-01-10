using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipboardWatcher;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ClipboardWatcherForm _watcherForm;
    private readonly ClipboardStore _store;
    private readonly ApiHost _apiHost;
    private HistoryForm? _historyForm;
    private readonly CancellationTokenSource _cts = new();
    private Task? _apiTask;

    public TrayApplicationContext(ClipboardStore store, int port)
    {
        _store = store;
        _apiHost = new ApiHost(store, port);

        _notifyIcon = BuildNotifyIcon(port);
        _watcherForm = new ClipboardWatcherForm();
        _watcherForm.ClipboardChanged += OnClipboardChanged;

        _apiTask = Task.Run(() => _apiHost.RunAsync(_cts.Token));
    }

    private NotifyIcon BuildNotifyIcon(int port)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add($"Listening on http://localhost:{port}", null, (_, _) => { });
        menu.Items.Add("Show History", null, (_, _) => ShowHistory());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        return new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = $"ClipboardWatcher (port {port})",
            ContextMenuStrip = menu
        };
    }

    private void OnClipboardChanged(object? sender, ClipboardSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Text))
        {
            _ = Task.Run(async () =>
            {
                var entry = await _store.SaveTextAsync(snapshot.Text!);
                if (entry is not null)
                {
                    await _apiHost.NotifyTextEntryAsync(entry);
                }
            });
        }
        else if (snapshot.ImageBytes is { Length: > 0 })
        {
            _ = Task.Run(async () =>
            {
                var entry = await _store.SaveImageAsync(snapshot.ImageBytes);
                if (entry is not null)
                {
                    await _apiHost.NotifyImageEntryAsync(entry);
                }
            });
        }
    }

    private void ShowHistory()
    {
        if (_historyForm is null || _historyForm.IsDisposed)
        {
            _historyForm = new HistoryForm(_store);
            _historyForm.FormClosed += (_, _) => _historyForm = null;
            _historyForm.Show();
        }
        else
        {
            if (!_historyForm.Visible)
            {
                _historyForm.Show();
            }

            _historyForm.WindowState = FormWindowState.Normal;
            _historyForm.BringToFront();
            _historyForm.Focus();
        }

        _ = _historyForm.RefreshHistoryAsync();
    }

    protected override void ExitThreadCore()
    {
        _cts.Cancel();
        _watcherForm.ClipboardChanged -= OnClipboardChanged;
        _watcherForm.Dispose();

        try
        {
            _apiTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore cancellation/aggregate exceptions on shutdown
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _cts.Dispose();

        base.ExitThreadCore();
    }
}
