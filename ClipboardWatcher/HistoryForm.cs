using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipboardWatcher;

public sealed class HistoryForm : Form
{
    private readonly ClipboardStore _store;
    private readonly ListView _listView;
    private readonly Button _refreshButton;
    private readonly Label _statusLabel;

    public HistoryForm(ClipboardStore store)
    {
        _store = store;
        Text = "Clipboard History";
        Width = 700;
        Height = 500;
        StartPosition = FormStartPosition.CenterScreen;

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false
        };
        _listView.Columns.Add("Type", 80);
        _listView.Columns.Add("Preview", 480);
        _listView.Columns.Add("Language", 100);
        _listView.Columns.Add("Created", 120);

        _refreshButton = new Button
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 90
        };
        _refreshButton.Click += async (_, _) => await LoadHistoryAsync();

        _statusLabel = new Label
        {
            Text = "Loading...",
            AutoSize = true,
            Dock = DockStyle.Left,
            Padding = new Padding(4, 8, 0, 0)
        };

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36
        };
        topPanel.Controls.Add(_refreshButton);
        topPanel.Controls.Add(_statusLabel);

        Controls.Add(_listView);
        Controls.Add(topPanel);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await LoadHistoryAsync();
    }

    public Task RefreshHistoryAsync() => LoadHistoryAsync();

    private async Task LoadHistoryAsync()
    {
        _refreshButton.Enabled = false;
        _statusLabel.Text = "Loading...";

        try
        {
            var textEntries = await _store.GetRecentTextAsync(100);
            var imageEntries = await _store.GetRecentImagesAsync(100);

            var items = new List<HistoryItem>(textEntries.Count + imageEntries.Count);
            items.AddRange(textEntries.Select(t => new HistoryItem("Text", BuildTextPreview(t.Content), t.Language, t.CreatedAt)));
            items.AddRange(imageEntries.Select(i => new HistoryItem("Image", BuildImagePreview(i.Data), null, i.CreatedAt)));

            var ordered = items
                .OrderByDescending(i => i.CreatedAt)
                .Take(100)
                .ToList();

            _listView.BeginUpdate();
            _listView.Items.Clear();
            foreach (var item in ordered)
            {
                var created = item.CreatedAt.ToLocalTime().ToString("g");
                var lvi = new ListViewItem(new[] { item.Type, item.Preview, item.Language ?? "Text", created });
                _listView.Items.Add(lvi);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
            return;
        }
        finally
        {
            _listView.EndUpdate();
            _refreshButton.Enabled = true;
            _statusLabel.Text = $"{_listView.Items.Count} items";
        }
    }

    private static string BuildTextPreview(string content)
    {
        var singleLine = content.ReplaceLineEndings(" ");
        if (singleLine.Length > 120)
        {
            singleLine = singleLine[..120] + "…";
        }
        return singleLine;
    }

    private static string BuildImagePreview(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var img = Image.FromStream(ms);
            var sizeKb = data.Length / 1024;
            return $"Image {img.Width}x{img.Height}, {sizeKb} KB";
        }
        catch
        {
            return $"Image ({data.Length} bytes)";
        }
    }

    private sealed record HistoryItem(string Type, string Preview, string? Language, DateTimeOffset CreatedAt);
}
