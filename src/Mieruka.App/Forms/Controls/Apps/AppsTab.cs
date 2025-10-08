using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Services;

namespace Mieruka.App.Forms.Controls.Apps;

public sealed class AppsTab : UserControl
{
    private readonly TextBox _txtSearch;
    private readonly DataGridView _grid;
    private readonly Button _btnSelectExecutable;
    private readonly Button _btnAdd;
    private readonly Button _btnRemove;
    private readonly Button _btnEditArgs;
    private readonly Button _btnOpen;
    private readonly Button _btnTest;
    private readonly TextBox _txtArgs;
    private readonly TextBox _txtPreview;
    private readonly BindingList<InstalledAppInfo> _allApps = new();
    private readonly BindingList<InstalledAppInfo> _filteredApps = new();
    private bool _suppressSelectionNotifications;
    private string _currentExecutablePath = string.Empty;
    private InstalledAppInfo? _selectedApp;

    public AppsTab()
    {
        Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _txtSearch = new TextBox
        {
            PlaceholderText = "Buscar aplicativos instalados...",
            Dock = DockStyle.Fill,
        };
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        layout.Controls.Add(_txtSearch, 0, 0);
        layout.SetColumnSpan(_txtSearch, 2);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
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
        _grid.SelectionChanged += (_, _) => NotifySelection();
        _grid.CellDoubleClick += (_, _) => ApplySelection();

        layout.Controls.Add(_grid, 0, 1);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        _btnSelectExecutable = new Button { Text = "Selecionar executável...", AutoSize = true };
        _btnSelectExecutable.Click += (_, _) => SelectExecutableFromDialog();

        _btnAdd = new Button { Text = "Adicionar", AutoSize = true };
        _btnAdd.Click += (_, _) => ApplySelection();

        _btnRemove = new Button { Text = "Remover", AutoSize = true };
        _btnRemove.Click += (_, _) => ClearSelection();

        _btnEditArgs = new Button { Text = "Editar Args", AutoSize = true };
        _btnEditArgs.Click += (_, _) => FocusArgs();

        _btnOpen = new Button { Text = "Abrir", AutoSize = true };
        _btnOpen.Click += (_, _) => OpenSelectedExecutableAsync();

        _btnTest = new Button { Text = "Testar", AutoSize = true };
        _btnTest.Click += (_, _) => TestSelectedExecutableAsync();

        buttonPanel.Controls.Add(_btnSelectExecutable);
        buttonPanel.Controls.Add(_btnAdd);
        buttonPanel.Controls.Add(_btnRemove);
        buttonPanel.Controls.Add(_btnEditArgs);
        buttonPanel.Controls.Add(_btnOpen);
        buttonPanel.Controls.Add(_btnTest);

        layout.Controls.Add(buttonPanel, 1, 1);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 8, 0, 0),
        };

        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblArgs = new Label
        {
            Text = "Args:",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
        };

        _txtArgs = new TextBox
        {
            Dock = DockStyle.Fill,
        };
        _txtArgs.TextChanged += (_, _) => HandleArgsChanged();

        var lblPreview = new Label
        {
            Text = "Linha final:",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
        };

        _txtPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
        };

        footer.Controls.Add(lblArgs, 0, 0);
        footer.Controls.Add(_txtArgs, 1, 0);
        footer.Controls.Add(lblPreview, 0, 1);
        footer.Controls.Add(_txtPreview, 1, 1);

        layout.Controls.Add(footer, 0, 2);
        layout.SetColumnSpan(footer, 2);

        Controls.Add(layout);
    }

    public event EventHandler<AppSelectionEventArgs>? ExecutableChosen;

    public event EventHandler? ExecutableCleared;

    public event EventHandler<string>? ArgumentsChanged;

    public string ExecutablePath
    {
        get => _currentExecutablePath;
        set
        {
            _currentExecutablePath = value ?? string.Empty;
            UpdatePreview();
        }
    }

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
        var apps = await Task.Run(InstalledAppsProvider.GetAll).ConfigureAwait(false);
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Populate(apps)));
        }
        else
        {
            Populate(apps);
        }
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

            ApplyFilterCore();
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

    private void NotifySelection()
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

        _filteredApps.RaiseListChangedEvents = false;
        _filteredApps.Clear();
        foreach (var app in filtered)
        {
            _filteredApps.Add(app);
        }
        _filteredApps.RaiseListChangedEvents = true;
        _filteredApps.ResetBindings();
    }

    private void ClearGridSelection()
    {
        if (_grid.IsDisposed)
        {
            return;
        }

        _grid.ClearSelection();
        _grid.CurrentCell = null;
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
        using var dialog = new OpenFileDialog
        {
            Filter = "Aplicativos (*.exe)|*.exe|Todos os arquivos (*.*)|*.*",
            Title = "Selecionar executável",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
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
            MessageBox.Show(
                this,
                $"Não foi possível abrir o aplicativo selecionado: {ex.Message}",
                "Abrir aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
            MessageBox.Show(
                this,
                "O teste de posicionamento não está disponível neste contexto.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
            MessageBox.Show(
                this,
                $"Não foi possível testar o aplicativo selecionado: {ex.Message}",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
            MessageBox.Show(
                this,
                "Selecione um executável antes de continuar.",
                "Aplicativos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }

        if (validateExecutable && !File.Exists(executablePath))
        {
            MessageBox.Show(
                this,
                "O executável informado não foi encontrado.",
                "Aplicativos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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
