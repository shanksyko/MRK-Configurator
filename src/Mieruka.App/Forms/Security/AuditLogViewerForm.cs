#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Security.Models;
using Mieruka.Core.Security.Services;
using Mieruka.App.Services.Ui;
using Serilog;

namespace Mieruka.App.Forms.Security;

/// <summary>Read-only viewer for the audit log with date/user/action filters and CSV export.</summary>
public sealed class AuditLogViewerForm : Form
{
    private static readonly ILogger Logger = Log.ForContext<AuditLogViewerForm>();
    private const int PageSize = 500;

    private readonly IAuditLogService _auditService;

    private readonly DateTimePicker _dtpFrom;
    private readonly DateTimePicker _dtpTo;
    private readonly ComboBox _cmbUser;
    private readonly TextBox _txtSearch;
    private readonly Button _btnFilter;
    private readonly Button _btnExportCsv;
    private readonly Button _btnLoadMore;
    private readonly Button _btnClose;
    private readonly DataGridView _grid;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;

    private List<AuditLogEntry> _entries = new();
    private int _currentSkip;

    public AuditLogViewerForm(IAuditLogService auditService)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));

        Text = "Log de Auditoria";
        ClientSize = new Size(900, 560);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(700, 420);
        StartPosition = FormStartPosition.CenterParent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        DoubleBuffered = true;

        // ── Filter bar ──
        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(6, 8, 6, 4),
        };

        var lblFrom = new Label { AutoSize = true, Text = "De:", Margin = new Padding(3, 6, 0, 0) };
        _dtpFrom = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Today.AddDays(-30),
            Width = 100,
        };

        var lblTo = new Label { AutoSize = true, Text = "Até:", Margin = new Padding(6, 6, 0, 0) };
        _dtpTo = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = DateTime.Today.AddDays(1),
            Width = 100,
        };

        var lblUser = new Label { AutoSize = true, Text = "Usuário:", Margin = new Padding(6, 6, 0, 0) };
        _cmbUser = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130,
        };
        _cmbUser.Items.Add("(todos)");
        _cmbUser.SelectedIndex = 0;

        var lblSearch = new Label { AutoSize = true, Text = "Ação:", Margin = new Padding(6, 6, 0, 0) };
        _txtSearch = new TextBox { Width = 130 };

        _btnFilter = new Button
        {
            Text = "Filtrar",
            Size = new Size(80, 26),
            Margin = new Padding(6, 1, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
        };
        _btnFilter.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 200);
        _btnFilter.Click += async (_, _) => await ReloadAsync(reset: true);

        filterPanel.Controls.AddRange(new Control[]
        {
            lblFrom, _dtpFrom, lblTo, _dtpTo,
            lblUser, _cmbUser, lblSearch, _txtSearch, _btnFilter,
        });

        // ── Grid ──
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colTimestamp", HeaderText = "Timestamp", FillWeight = 18, MinimumWidth = 130, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colUsername", HeaderText = "Usuário", FillWeight = 13, MinimumWidth = 80, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colAction", HeaderText = "Ação", FillWeight = 20, MinimumWidth = 100, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colEntityType", HeaderText = "Tipo", FillWeight = 12, MinimumWidth = 70, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colEntityId", HeaderText = "ID", FillWeight = 9, MinimumWidth = 60, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDetails", HeaderText = "Detalhes", FillWeight = 28, MinimumWidth = 120, ReadOnly = true });
        DoubleBufferingHelper.EnableOptimizedDoubleBuffering(_grid);

        // ── Bottom button bar ──
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };

        _btnLoadMore = new Button
        {
            Text = "Carregar mais",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(245, 245, 245),
            Enabled = false,
        };
        _btnLoadMore.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnLoadMore.Click += async (_, _) => await ReloadAsync(reset: false);

        _btnExportCsv = new Button
        {
            Text = "Exportar CSV",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(245, 245, 245),
        };
        _btnExportCsv.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnExportCsv.Click += OnExportCsvClicked;

        _btnClose = new Button
        {
            Text = "Fechar",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(245, 245, 245),
        };
        _btnClose.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnClose.Click += (_, _) => Close();

        bottomPanel.Controls.AddRange(new Control[] { _btnLoadMore, _btnExportCsv, _btnClose });

        // ── Status ──
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Pronto.");
        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_grid);
        Controls.Add(filterPanel);
        Controls.Add(bottomPanel);
        Controls.Add(_statusStrip);

        CancelButton = _btnClose;
        Shown += async (_, _) =>
        {
            await LoadUsersAsync();
            await ReloadAsync(reset: true);
        };
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var logs = await _auditService.GetLogsAsync(limit: 200);
            var usernames = logs.Select(l => l.Username)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(u => u)
                .ToList();

            _cmbUser.Items.Clear();
            _cmbUser.Items.Add("(todos)");
            foreach (var u in usernames)
            {
                _cmbUser.Items.Add(u);
            }

            _cmbUser.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not pre-load user list for audit log filter.");
        }
    }

    private async Task ReloadAsync(bool reset)
    {
        if (reset)
        {
            _entries.Clear();
            _currentSkip = 0;
            _grid.Rows.Clear();
        }

        _btnFilter.Enabled = false;
        _btnLoadMore.Enabled = false;

        try
        {
            var from = _dtpFrom.Value.Date;
            var to = _dtpTo.Value.Date.AddDays(1);
            var selectedUser = _cmbUser.SelectedIndex > 0 ? _cmbUser.SelectedItem as string : null;
            var searchText = _txtSearch.Text.Trim();

            SetStatus("Carregando...");

            var all = await _auditService.GetLogsAsync(
                from: from,
                to: to,
                userId: null,
                limit: PageSize);

            var filtered = all.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(selectedUser))
            {
                filtered = filtered.Where(e => string.Equals(e.Username, selectedUser, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(e =>
                    (e.Action ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            var page = filtered.Skip(_currentSkip).Take(PageSize).ToList();
            _currentSkip += page.Count;
            _entries.AddRange(page);

            _grid.SuspendLayout();
            foreach (var entry in page)
            {
                _grid.Rows.Add(
                    entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    entry.Username,
                    entry.Action,
                    entry.EntityType ?? string.Empty,
                    entry.EntityId ?? string.Empty,
                    entry.Details ?? string.Empty);
            }
            _grid.ResumeLayout();

            var hasMore = page.Count == PageSize;
            _btnLoadMore.Enabled = hasMore;
            SetStatus($"{_entries.Count} registro(s) exibido(s).{(hasMore ? " Há mais registros — clique em \"Carregar mais\"." : string.Empty)}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao carregar log de auditoria.");
            SetStatus("Erro ao carregar log.");
        }
        finally
        {
            _btnFilter.Enabled = true;
        }
    }

    private void OnExportCsvClicked(object? sender, EventArgs e)
    {
        if (_entries.Count == 0)
        {
            MessageBox.Show("Nenhum registro para exportar.", "Exportar CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Exportar Log de Auditoria",
            Filter = "CSV (*.csv)|*.csv|Todos os arquivos (*.*)|*.*",
            FileName = $"auditoria_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp;Usuário;Ação;Tipo;ID;Detalhes");
            foreach (var entry in _entries)
            {
                sb.AppendLine(string.Join(";",
                    CsvEscape(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                    CsvEscape(entry.Username),
                    CsvEscape(entry.Action),
                    CsvEscape(entry.EntityType ?? string.Empty),
                    CsvEscape(entry.EntityId ?? string.Empty),
                    CsvEscape(entry.Details ?? string.Empty)));
            }

            // UTF-8 with BOM so Excel recognises encoding and accented characters.
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus($"Exportado: {dlg.FileName}");
            Logger.Information("Audit log exported to {Path}", dlg.FileName);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao exportar CSV.");
            MessageBox.Show($"Erro ao exportar: {ex.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(';') || value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private void SetStatus(string message) => _statusLabel.Text = message;
}
