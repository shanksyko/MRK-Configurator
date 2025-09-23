using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private readonly TextBox _txtArgs;
    private readonly TextBox _txtPreview;
    private readonly BindingList<InstalledAppInfo> _allApps = new();
    private readonly BindingList<InstalledAppInfo> _filteredApps = new();
    private readonly InstalledAppsProvider _provider = new();
    private string _currentExecutablePath = string.Empty;

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

        buttonPanel.Controls.Add(_btnSelectExecutable);
        buttonPanel.Controls.Add(_btnAdd);
        buttonPanel.Controls.Add(_btnRemove);
        buttonPanel.Controls.Add(_btnEditArgs);

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
        var apps = await Task.Run(() => _provider.GetAll()).ConfigureAwait(false);
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
        _allApps.Clear();
        foreach (var app in apps)
        {
            _allApps.Add(app);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
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

    private void NotifySelection()
    {
        if (_grid.CurrentRow?.DataBoundItem is InstalledAppInfo app)
        {
            ExecutableChosen?.Invoke(this, new AppSelectionEventArgs(app.Name, app.ExecutablePath, app));
        }
    }

    private void ApplySelection()
    {
        if (_grid.CurrentRow?.DataBoundItem is InstalledAppInfo app)
        {
            ExecutableChosen?.Invoke(this, new AppSelectionEventArgs(app.Name, app.ExecutablePath, app));
        }
    }

    private void ClearSelection()
    {
        ExecutableCleared?.Invoke(this, EventArgs.Empty);
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

        var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
        ExecutableChosen?.Invoke(this, new AppSelectionEventArgs(fileName, dialog.FileName, null));
    }
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
