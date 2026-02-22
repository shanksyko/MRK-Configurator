using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Serilog;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Forms.Controls.Apps;

public sealed class AppsTab : WinForms.UserControl
{
    private static readonly ILogger Logger = Log.ForContext<AppsTab>();

    private readonly WinForms.TextBox _txtSearch;
    private readonly DataGridView _grid;
    private readonly WinForms.Button _btnSelectExecutable;
    private readonly WinForms.Button _btnAdd;
    private readonly WinForms.Button _btnRemove;
    private readonly WinForms.Button _btnEditArgs;
    private readonly WinForms.Button _btnOpen;
    private readonly WinForms.Button _btnTest;
    private readonly WinForms.TextBox _txtArgs;
    private readonly WinForms.TextBox _txtPreview;
    private readonly WinForms.Label _statusLabel;
    private readonly BindingList<InstalledAppInfo> _allApps = new();
    private readonly BindingList<InstalledAppInfo> _filteredApps = new();
    private bool _suppressSelectionNotifications;
    private bool _settingCurrentCell;
    private string _currentExecutablePath = string.Empty;
    private InstalledAppInfo? _selectedApp;

    public AppsTab()
    {
        Dock = WinForms.DockStyle.Fill;

        var layout = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new WinForms.Padding(8),
        };

        layout.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Absolute, 200F));
        layout.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new WinForms.RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));

        _txtSearch = new WinForms.TextBox
        {
            PlaceholderText = "Buscar aplicativos instalados...",
            Dock = WinForms.DockStyle.Fill,
        };
        _txtSearch.TextChanged += TxtSearch_TextChanged;

        layout.Controls.Add(_txtSearch, 0, 0);
        layout.SetColumnSpan(_txtSearch, 2);

        _grid = new DataGridView
        {
            Dock = WinForms.DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            BorderStyle = WinForms.BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Drawing.Color.FromArgb(230, 230, 230),
            BackgroundColor = Drawing.SystemColors.Window,
            RowTemplate = { Height = 32 },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Drawing.Color.FromArgb(52, 73, 94),
                ForeColor = Drawing.Color.White,
                Font = new Drawing.Font("Segoe UI Semibold", 9F, Drawing.FontStyle.Bold),
                Padding = new WinForms.Padding(6, 4, 6, 4),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
            },
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 36,
            EnableHeadersVisualStyles = false,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Drawing.Font("Segoe UI", 9F),
                Padding = new WinForms.Padding(6, 2, 6, 2),
                SelectionBackColor = Drawing.Color.FromArgb(52, 152, 219),
                SelectionForeColor = Drawing.Color.White,
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Drawing.Color.FromArgb(245, 248, 255),
            },
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Nome",
            DataPropertyName = nameof(InstalledAppInfo.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 40,
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Versão",
            DataPropertyName = nameof(InstalledAppInfo.Version),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Fornecedor",
            DataPropertyName = nameof(InstalledAppInfo.Vendor),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Caminho",
            DataPropertyName = nameof(InstalledAppInfo.ExecutablePath),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 60,
        });

        _grid.DataSource = _filteredApps;
        _grid.CellDoubleClick += Grid_CellDoubleClick;

        layout.Controls.Add(_grid, 0, 1);

        var buttonPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        _btnSelectExecutable = new WinForms.Button { Text = "Selecionar aplicativo...", AutoSize = true };
        _btnSelectExecutable.Click += (_, _) => SelectExecutableFromDialog();

        _btnAdd = new WinForms.Button { Text = "Adicionar", AutoSize = true };
        _btnAdd.Click += (_, _) => ApplySelection();

        _btnRemove = new WinForms.Button { Text = "Remover", AutoSize = true };
        _btnRemove.Click += (_, _) => ClearSelection();

        _btnEditArgs = new WinForms.Button { Text = "Editar Args", AutoSize = true };
        _btnEditArgs.Click += (_, _) => FocusArgs();

        _btnOpen = new WinForms.Button { Text = "Abrir", AutoSize = true };
        _btnOpen.Click += (_, _) => OpenSelectedExecutableAsync();

        _btnTest = new WinForms.Button { Text = "Testar", AutoSize = true };
        _btnTest.Click += (_, _) => TestSelectedExecutableAsync();

        buttonPanel.Controls.Add(_btnSelectExecutable);
        buttonPanel.Controls.Add(_btnAdd);
        buttonPanel.Controls.Add(_btnRemove);
        buttonPanel.Controls.Add(_btnEditArgs);
        buttonPanel.Controls.Add(_btnOpen);
        buttonPanel.Controls.Add(_btnTest);

        layout.Controls.Add(buttonPanel, 1, 1);

        var footer = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new WinForms.Padding(0, 8, 0, 0),
        };

        footer.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Absolute, 80F));
        footer.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 100F));

        footer.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));

        var lblArgs = new WinForms.Label
        {
            Text = "Args:",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Dock = WinForms.DockStyle.Fill,
        };

        _txtArgs = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
        };
        _txtArgs.TextChanged += (_, _) => HandleArgsChanged();

        var lblPreview = new WinForms.Label
        {
            Text = "Linha final:",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Dock = WinForms.DockStyle.Fill,
        };

        _txtPreview = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            ReadOnly = true,
        };

        footer.Controls.Add(lblArgs, 0, 0);
        footer.Controls.Add(_txtArgs, 1, 0);
        footer.Controls.Add(lblPreview, 0, 1);
        footer.Controls.Add(_txtPreview, 1, 1);
        _statusLabel = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Fill,
            AutoSize = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = System.Drawing.SystemColors.GrayText,
            Padding = new WinForms.Padding(0, 4, 0, 0),
        };
        footer.Controls.Add(_statusLabel, 0, 2);
        footer.SetColumnSpan(_statusLabel, 2);

        layout.Controls.Add(footer, 0, 2);
        layout.SetColumnSpan(footer, 2);

        Controls.Add(layout);

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        DoubleBuffered = true;
        DoubleBufferingHelper.EnableOptimizedDoubleBuffering(_grid);
    }

    public event EventHandler<AppSelectionEventArgs>? ExecutableChosen;

    public event EventHandler? ExecutableCleared;

    public event EventHandler<string>? ArgumentsChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ExecutablePath
    {
        get => _currentExecutablePath;
        set
        {
            _currentExecutablePath = value ?? string.Empty;
            UpdatePreview();
        }
    }

    /// <summary>
    /// Tries to select the grid row matching the given executable path.
    /// Call after the apps list has been populated.
    /// </summary>
    public void TrySelectByPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || _grid.IsDisposed)
        {
            return;
        }

        _suppressSelectionNotifications = true;
        try
        {
            for (var i = 0; i < _grid.Rows.Count; i++)
            {
                if (_grid.Rows[i].DataBoundItem is InstalledAppInfo app &&
                    string.Equals(app.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    _grid.ClearSelection();
                    _grid.Rows[i].Selected = true;
                    _settingCurrentCell = true;
                    try
                    {
                        _grid.CurrentCell = _grid.Rows[i].Cells[0];
                    }
                    finally
                    {
                        _settingCurrentCell = false;
                    }

                    if (i < _grid.FirstDisplayedScrollingRowIndex ||
                        i >= _grid.FirstDisplayedScrollingRowIndex + _grid.DisplayedRowCount(false))
                    {
                        _grid.FirstDisplayedScrollingRowIndex = i;
                    }

                    _selectedApp = app;
                    return;
                }
            }
        }
        finally
        {
            _suppressSelectionNotifications = false;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Arguments
    {
        get => _txtArgs.Text;
        set
        {
            _txtArgs.Text = value ?? string.Empty;
            UpdatePreview();
        }
    }

    public async Task LoadInstalledAppsAsync()
    {
        try
        {
            var apps = await Task.Run(InstalledAppsProvider.GetAll).ConfigureAwait(false);
            if (IsDisposed)
            {
                return;
            }

            MarshalToUiSafe(() => Populate(apps));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao carregar a lista de aplicativos instalados.");

            if (IsDisposed)
            {
                return;
            }

            void ShowError()
            {
                WinForms.MessageBox.Show(
                    this,
                    "Não foi possível carregar a lista de aplicativos instalados. Consulte o log para mais detalhes.",
                    "Aplicativos instalados",
                    WinForms.MessageBoxButtons.OK,
                    WinForms.MessageBoxIcon.Warning);
            }

            MarshalToUiSafe(ShowError);
        }
    }

    public void SetInstalledApps(IReadOnlyList<InstalledAppInfo> apps)
    {
        if (apps is null)
        {
            throw new ArgumentNullException(nameof(apps));
        }

        if (IsDisposed)
        {
            return;
        }

        MarshalToUiSafe(() => Populate(apps));
    }

    private void Populate(IReadOnlyList<InstalledAppInfo> apps)
    {
        _suppressSelectionNotifications = true;
        try
        {
            _allApps.Clear();
            foreach (var app in apps)
            {
                _allApps.Add(app);
            }

            PopulateFilteredList(_allApps);
            ClearGridSelection();
        }
        finally
        {
            _suppressSelectionNotifications = false;
        }
    }

    private void ApplyFilter()
    {
        _suppressSelectionNotifications = true;
        try
        {
            ApplyFilterCore();
            ClearGridSelection();
        }
        finally
        {
            _suppressSelectionNotifications = false;
        }
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _suppressSelectionNotifications)
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].DataBoundItem is not InstalledAppInfo app)
        {
            return;
        }

        var result = WinForms.MessageBox.Show(
            this,
            $"Deseja adicionar o aplicativo \"{app.Name}\"?",
            "Adicionar aplicativo",
            WinForms.MessageBoxButtons.YesNo,
            WinForms.MessageBoxIcon.Question);

        if (result == WinForms.DialogResult.Yes)
        {
            ApplySelection();
        }
    }

    private void ApplySelection()
    {
        if (_suppressSelectionNotifications)
        {
            return;
        }

        if (_grid.CurrentRow?.DataBoundItem is InstalledAppInfo app)
        {
            _selectedApp = app;
            ExecutableChosen?.Invoke(this, new AppSelectionEventArgs(app.Name, app.ExecutablePath, app));
        }
    }

    private void ClearSelection()
    {
        _suppressSelectionNotifications = true;
        try
        {
            ClearGridSelection();
        }
        finally
        {
            _suppressSelectionNotifications = false;
        }

        _selectedApp = null;
        ExecutableCleared?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilterCore()
    {
        var term = _txtSearch.Text?.Trim();
        IEnumerable<InstalledAppInfo> filtered = _allApps;

        if (!string.IsNullOrWhiteSpace(term))
        {
            filtered = filtered.Where(app =>
                app.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(app.Vendor) && app.Vendor.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                app.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        PopulateFilteredList(filtered);
    }

    private void TxtSearch_TextChanged(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtSearch.Text))
        {
            _suppressSelectionNotifications = true;
            try
            {
                PopulateFilteredList(_allApps);
                ClearGridSelection();
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            return;
        }

        ApplyFilter();
    }

    private void PopulateFilteredList(IEnumerable<InstalledAppInfo> apps)
    {
        _grid.SuspendLayout();
        _filteredApps.RaiseListChangedEvents = false;
        _filteredApps.Clear();
        foreach (var app in apps)
        {
            _filteredApps.Add(app);
        }
        _filteredApps.RaiseListChangedEvents = true;
        _filteredApps.ResetBindings();
        _grid.ResumeLayout(false);

        UpdateStatusLabel();
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel.IsDisposed)
        {
            return;
        }

        var count = _filteredApps.Count;
        var suffix = count == 1 ? "aplicativo encontrado" : "aplicativos encontrados";
        _statusLabel.Text = $"{count} {suffix}";
    }

    private void ClearGridSelection()
    {
        if (_grid.IsDisposed)
        {
            return;
        }

        _grid.ClearSelection();
        ClearGridCurrentCellSafe();
    }

    private void MarshalToUiSafe(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // Control was disposed between the check and the invoke.
        }
        catch (InvalidOperationException)
        {
            // Handle not yet created or already destroyed.
        }
    }

    // CurrentCell changes are centralized to avoid reentrancy issues:
    // ClearGridSelection() clears via ClearGridCurrentCellSafe().
    private void ClearGridCurrentCellSafe()
    {
        if (_settingCurrentCell)
        {
            return;
        }

        try
        {
            _settingCurrentCell = true;
            _grid.CurrentCell = null;
        }
        finally
        {
            _settingCurrentCell = false;
        }
    }

    private void FocusArgs()
    {
        if (!_txtArgs.IsDisposed)
        {
            _txtArgs.Focus();
            _txtArgs.SelectAll();
        }
    }

    private void HandleArgsChanged()
    {
        UpdatePreview();
        ArgumentsChanged?.Invoke(this, _txtArgs.Text);
    }

    private void UpdatePreview()
    {
        var command = string.IsNullOrWhiteSpace(_currentExecutablePath)
            ? string.Empty
            : QuoteIfNeeded(_currentExecutablePath);

        if (!string.IsNullOrWhiteSpace(_txtArgs.Text))
        {
            command = string.IsNullOrWhiteSpace(command)
                ? _txtArgs.Text
                : $"{command} {_txtArgs.Text}";
        }

        _txtPreview.Text = command;
    }

    private static string QuoteIfNeeded(string path)
    {
        return path.Contains(' ') && !path.Contains('"')
            ? $"\"{path}\""
            : path;
    }

    private void SelectExecutableFromDialog()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Filter = "Aplicativos (*.exe)|*.exe|Todos os arquivos (*.*)|*.*",
            Title = "Selecionar executável",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
        {
            return;
        }

        _suppressSelectionNotifications = true;
        try
        {
            ClearGridSelection();
        }
        finally
        {
            _suppressSelectionNotifications = false;
        }

        _selectedApp = null;
        var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
        ExecutableChosen?.Invoke(this, new AppSelectionEventArgs(fileName, dialog.FileName, null));
    }

    private async void OpenSelectedExecutableAsync()
    {
        var args = BuildExecutionArgs(validateExecutable: true);
        if (args is null)
        {
            return;
        }

        if (!_btnOpen.IsDisposed)
        {
            _btnOpen.Enabled = false;
        }

        try
        {
            if (OpenRequested is not null)
            {
                await InvokeAsync(OpenRequested, this, args).ConfigureAwait(true);
            }
            else
            {
                await LaunchProcessAsync(args).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                this,
                $"Não foi possível abrir o aplicativo selecionado: {ex.Message}",
                "Abrir aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            if (!_btnOpen.IsDisposed)
            {
                _btnOpen.Enabled = true;
            }
        }
    }

    private async void TestSelectedExecutableAsync()
    {
        var args = BuildExecutionArgs(validateExecutable: false);
        if (args is null)
        {
            return;
        }

        if (TestRequested is null)
        {
            WinForms.MessageBox.Show(
                this,
                "O teste de posicionamento não está disponível neste contexto.",
                "Teste de aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return;
        }

        if (!_btnTest.IsDisposed)
        {
            _btnTest.Enabled = false;
        }

        try
        {
            await InvokeAsync(TestRequested, this, args).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                this,
                $"Não foi possível testar o aplicativo selecionado: {ex.Message}",
                "Teste de aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            if (!_btnTest.IsDisposed)
            {
                _btnTest.Enabled = true;
            }
        }
    }

    private AppExecutionRequestEventArgs? BuildExecutionArgs(bool validateExecutable)
    {
        var executablePath = _currentExecutablePath.Trim();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            WinForms.MessageBox.Show(
                this,
                "Selecione um executável antes de continuar.",
                "Aplicativos",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return null;
        }

        if (validateExecutable && !File.Exists(executablePath))
        {
            WinForms.MessageBox.Show(
                this,
                "O executável informado não foi encontrado.",
                "Aplicativos",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Warning);
            return null;
        }

        var arguments = string.IsNullOrWhiteSpace(_txtArgs.Text) ? null : _txtArgs.Text;
        return new AppExecutionRequestEventArgs(executablePath, arguments, _selectedApp);
    }

    private static Task LaunchProcessAsync(AppExecutionRequestEventArgs args)
    {
        return Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = args.ExecutablePath,
                UseShellExecute = false,
            };

            if (!string.IsNullOrWhiteSpace(args.Arguments))
            {
                startInfo.Arguments = args.Arguments;
            }

            var workingDirectory = Path.GetDirectoryName(args.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Não foi possível iniciar o aplicativo selecionado.");
            }
        });
    }

    private static Task InvokeAsync(AppExecutionHandler? handler, object? sender, AppExecutionRequestEventArgs args)
    {
        if (handler is null)
        {
            return Task.CompletedTask;
        }

        var delegates = handler.GetInvocationList();
        if (delegates.Length == 1)
        {
            return ((AppExecutionHandler)delegates[0])(sender, args);
        }

        var tasks = delegates
            .Cast<AppExecutionHandler>()
            .Select(d => d(sender, args));

        return Task.WhenAll(tasks);
    }

    public event AppExecutionHandler? OpenRequested;

    public event AppExecutionHandler? TestRequested;
}

public sealed class AppSelectionEventArgs : EventArgs
{
    public AppSelectionEventArgs(string? name, string executablePath, InstalledAppInfo? app)
    {
        Name = name;
        ExecutablePath = executablePath;
        App = app;
    }

    public string? Name { get; }

    public string ExecutablePath { get; }

    public InstalledAppInfo? App { get; }
}

public delegate Task AppExecutionHandler(object? sender, AppExecutionRequestEventArgs e);

public sealed class AppExecutionRequestEventArgs : EventArgs
{
    public AppExecutionRequestEventArgs(string executablePath, string? arguments, InstalledAppInfo? app)
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
        App = app;
    }

    public string ExecutablePath { get; }

    public string? Arguments { get; }

    public InstalledAppInfo? App { get; }
}
