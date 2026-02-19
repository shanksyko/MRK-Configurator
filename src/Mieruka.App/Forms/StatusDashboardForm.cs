#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Services;

namespace Mieruka.App.Forms;

/// <summary>
/// Displays real-time status of monitored applications and sites.
/// </summary>
internal sealed class StatusDashboardForm : Form
{
    private readonly ListView _listView;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Func<IReadOnlyList<WatchdogStatusEntry>> _getSnapshot;

    public StatusDashboardForm(Func<IReadOnlyList<WatchdogStatusEntry>> getSnapshot)
    {
        _getSnapshot = getSnapshot ?? throw new ArgumentNullException(nameof(getSnapshot));

        Text = "Dashboard de Status";
        Size = new Size(750, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(600, 350);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
        };
        _listView.Columns.Add("Nome", 180);
        _listView.Columns.Add("Tipo", 80);
        _listView.Columns.Add("PID", 70);
        _listView.Columns.Add("Status", 100);
        _listView.Columns.Add("Falhas", 70);
        _listView.Columns.Add("Último Check", 150);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(8),
        };

        _btnRefresh = new Button { Text = "Atualizar", AutoSize = true };
        _btnRefresh.Click += (_, _) => RefreshData();
        buttonPanel.Controls.Add(_btnRefresh);

        _btnClose = new Button { Text = "Fechar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(_btnClose);

        Controls.Add(_listView);
        Controls.Add(buttonPanel);
        CancelButton = _btnClose;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => RefreshData();

        Load += (_, _) =>
        {
            RefreshData();
            _refreshTimer.Start();
        };

        FormClosing += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshData()
    {
        try
        {
            var entries = _getSnapshot();
            _listView.BeginUpdate();
            _listView.Items.Clear();

            foreach (var entry in entries)
            {
                var statusText = entry.IsAlive ? "Online" : "Offline";
                var lastCheck = entry.LastHealthCheck?.ToLocalTime().ToString("HH:mm:ss") ?? "—";

                var item = new ListViewItem(new[]
                {
                    entry.Name,
                    entry.Type,
                    entry.ProcessId > 0 ? entry.ProcessId.ToString() : "—",
                    statusText,
                    entry.FailureCount.ToString(),
                    lastCheck,
                });

                item.ForeColor = entry.IsAlive ? Color.DarkGreen : Color.Red;
                _listView.Items.Add(item);
            }

            _listView.EndUpdate();
        }
        catch
        {
            // Ignore refresh errors for dashboard resilience.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Status entry for a monitored application or site.
/// </summary>
public sealed class WatchdogStatusEntry
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public bool IsAlive { get; init; }
    public int FailureCount { get; init; }
    public DateTimeOffset? LastHealthCheck { get; init; }
}
