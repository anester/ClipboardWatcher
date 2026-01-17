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
    private readonly object _clipboardGate = new();
    private string? _ignoreClipboardText;
    private DateTimeOffset _ignoreClipboardUntil;
    private DateTimeOffset? _lastClipboardSetAt;
    private string? _lastClipboardSetText;
    private HistoryForm? _historyForm;
    private readonly CancellationTokenSource _cts = new();
    private Task? _apiTask;

    public TrayApplicationContext(ClipboardStore store, int port)
    {
        _store = store;
        _apiHost = new ApiHost(store, port, SetClipboardTextAsync);

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
        if (snapshot.Text is not null)
        {
            var now = DateTimeOffset.UtcNow;
            lock (_clipboardGate)
            {
                if (_lastClipboardSetAt.HasValue)
                {
                    var deltaMs = (now - _lastClipboardSetAt.Value).TotalMilliseconds;
                    Console.WriteLine($"[Clipboard] Change at {now:O}, {deltaMs:F0} ms since set. Match={string.Equals(snapshot.Text, _lastClipboardSetText, StringComparison.Ordinal)}");
                }

                if (_ignoreClipboardText is not null
                    && now <= _ignoreClipboardUntil
                    && string.Equals(snapshot.Text, _ignoreClipboardText, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[Clipboard] Ignored change at {now:O}.");
                    return;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Text))
        {
            _ = Task.Run(async () =>
            {
                var language = ClipboardLanguageDetector.Detect(snapshot.Text);
                var entry = await _store.SaveTextAsync(snapshot.Text!, language);
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

    private Task SetClipboardTextAsync(string text)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_watcherForm.IsDisposed)
        {
            tcs.SetException(new InvalidOperationException("Clipboard watcher is not available."));
            return tcs.Task;
        }

        try
        {
            _watcherForm.BeginInvoke(new Action(() =>
            {
                try
                {
                    lock (_clipboardGate)
                    {
                        _ignoreClipboardText = text;
                        _ignoreClipboardUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                        _lastClipboardSetAt = DateTimeOffset.UtcNow;
                        _lastClipboardSetText = text;
                    }
                    Console.WriteLine($"[Clipboard] Set text at {_lastClipboardSetAt:O}, ignore until {_ignoreClipboardUntil:O}.");
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }));
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }

        return tcs.Task;
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
