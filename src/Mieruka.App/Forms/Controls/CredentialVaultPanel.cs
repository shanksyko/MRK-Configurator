using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms.Controls;

public sealed class CredentialVaultPanel : UserControl
{
    private readonly SecretsProvider _secretsProvider;
    private readonly UiSecretsBridge _secretsBridge;
    private readonly BindingList<CredentialSummary> _entries = new();
    private readonly DataGridView _grid;
    private readonly TableLayoutPanel _rootLayout;
    private readonly TableLayoutPanel _editorLayout;
    private readonly TextBox _userBox;
    private readonly TextBox _passwordBox;
    private readonly TextBox _totpBox;
    private readonly Label _scopeLabel;
    private readonly Button _openGlobalButton;
    private string? _scopeSiteId;
    private string? _currentEditorSiteId;

    public event EventHandler<string>? TestLoginRequested;
    public event EventHandler? OpenGlobalVaultRequested;

    public CredentialVaultPanel(SecretsProvider secretsProvider, UiSecretsBridge secretsBridge)
    {
        _secretsProvider = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
        _secretsBridge = secretsBridge ?? throw new ArgumentNullException(nameof(secretsBridge));

        LayoutHelpers.ApplyStandardLayout(this);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            DataSource = _entries,
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CredentialSummary.SiteId),
            HeaderText = "SiteId",
            Width = 160,
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(CredentialSummary.HasUser),
            HeaderText = "HasUser",
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(CredentialSummary.HasPassword),
            HeaderText = "HasPass",
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(CredentialSummary.HasTotp),
            HeaderText = "HasTOTP",
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CredentialSummary.UpdatedAtDisplay),
            HeaderText = "UpdatedAt",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _grid.SelectionChanged += (_, _) => UpdateEditorFromSelection();

        _editorLayout = LayoutHelpers.CreateStandardTableLayout();
        _editorLayout.RowCount = 5;
        _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _scopeLabel = new Label { AutoSize = true };
        _editorLayout.Controls.Add(_scopeLabel, 0, 0);

        _userBox = CreateSecretTextBox("Usuário");
        _editorLayout.Controls.Add(_userBox, 0, 1);

        _passwordBox = CreateSecretTextBox("Senha");
        _editorLayout.Controls.Add(_passwordBox, 0, 2);

        _totpBox = CreateSecretTextBox("TOTP");
        _editorLayout.Controls.Add(_totpBox, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var saveButton = new Button { Text = "Salvar no Cofre", AutoSize = true };
        saveButton.Click += (_, _) => SaveCurrent();
        var deleteButton = new Button { Text = "Apagar", AutoSize = true };
        deleteButton.Click += (_, _) => DeleteCurrent();
        var testButton = new Button { Text = "Testar Login", AutoSize = true };
        testButton.Click += (_, _) => TriggerTestLogin();
        _openGlobalButton = new Button { Text = "Abrir Cofre Global…", AutoSize = true };
        _openGlobalButton.Click += (_, _) =>
        {
            ScopeSiteId = null;
            OpenGlobalVaultRequested?.Invoke(this, EventArgs.Empty);
        };

        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(deleteButton);
        buttons.Controls.Add(testButton);
        buttons.Controls.Add(_openGlobalButton);
        _editorLayout.Controls.Add(buttons, 0, 4);

        _rootLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        _rootLayout.Controls.Add(_grid, 0, 0);
        _rootLayout.Controls.Add(_editorLayout, 1, 0);

        Controls.Add(_rootLayout);

        _secretsProvider.CredentialsChanged += OnCredentialsChanged;
        UpdateScope();
    }

    public string? ScopeSiteId
    {
        get => _scopeSiteId;
        set
        {
            if (_scopeSiteId == value)
            {
                return;
            }

            _scopeSiteId = value;
            UpdateScope();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _secretsProvider.CredentialsChanged -= OnCredentialsChanged;
        }

        base.Dispose(disposing);
    }

    private static TextBox CreateSecretTextBox(string placeholder)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = placeholder,
        };
    }

    private void UpdateScope()
    {
        var isScoped = !string.IsNullOrEmpty(_scopeSiteId);
        _grid.Visible = !isScoped;
        _grid.Enabled = !isScoped;
        _openGlobalButton.Visible = isScoped;

        if (isScoped)
        {
            SetEditorSite(_scopeSiteId);
        }
        else
        {
            UpdateEditorFromSelection();
        }
    }

    private void UpdateEditorFromSelection()
    {
        if (_scopeSiteId is not null)
        {
            return;
        }

        var siteId = _grid.CurrentRow?.DataBoundItem is CredentialSummary summary ? summary.SiteId : null;
        SetEditorSite(siteId);
    }

    private void SetEditorSite(string? siteId)
    {
        _currentEditorSiteId = siteId;
        var hasSite = !string.IsNullOrEmpty(siteId);
        _scopeLabel.Text = hasSite ? $"Escopo: {siteId}" : "Selecione um site";
        _userBox.Enabled = hasSite;
        _passwordBox.Enabled = hasSite;
        _totpBox.Enabled = hasSite;

        if (hasSite)
        {
            UpdateSummaryFor(siteId!);
        }
        else
        {
            _userBox.Clear();
            _passwordBox.Clear();
            _totpBox.Clear();
        }
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrEmpty(_currentEditorSiteId))
        {
            return;
        }

        _secretsBridge.Save(_currentEditorSiteId, _userBox, _passwordBox, _totpBox);
        UpdateSummaryFor(_currentEditorSiteId);
    }

    private void DeleteCurrent()
    {
        if (string.IsNullOrEmpty(_currentEditorSiteId))
        {
            return;
        }

        _secretsBridge.Delete(_currentEditorSiteId);
        UpdateSummaryFor(_currentEditorSiteId);
    }

    private void TriggerTestLogin()
    {
        if (!string.IsNullOrEmpty(_currentEditorSiteId))
        {
            TestLoginRequested?.Invoke(this, _currentEditorSiteId);
        }
    }

    private void OnCredentialsChanged(object? sender, CredentialChangedEventArgs e)
    {
        UpdateSummaryFor(e.SiteId);
    }

    private void UpdateSummaryFor(string siteId)
    {
        var summary = EnsureSummary(siteId);

        using var username = _secretsBridge.LoadUser(siteId);
        using var password = _secretsBridge.LoadPass(siteId);
        using var totp = _secretsBridge.LoadTotp(siteId);

        summary.Apply(
            username is { Length: > 0 },
            password is { Length: > 0 },
            totp is { Length: > 0 });
    }

    private CredentialSummary EnsureSummary(string siteId)
    {
        var summary = _entries.FirstOrDefault(entry => string.Equals(entry.SiteId, siteId, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            summary = new CredentialSummary(siteId);
            _entries.Add(summary);
        }

        return summary;
    }

    private sealed class CredentialSummary : INotifyPropertyChanged
    {
        private bool _hasUser;
        private bool _hasPassword;
        private bool _hasTotp;
        private DateTime? _updatedAt;

        public CredentialSummary(string siteId)
        {
            SiteId = siteId;
        }

        public string SiteId { get; }

        public bool HasUser
        {
            get => _hasUser;
            private set => SetField(ref _hasUser, value, nameof(HasUser));
        }

        public bool HasPassword
        {
            get => _hasPassword;
            private set => SetField(ref _hasPassword, value, nameof(HasPassword));
        }

        public bool HasTotp
        {
            get => _hasTotp;
            private set => SetField(ref _hasTotp, value, nameof(HasTotp));
        }

        public DateTime? UpdatedAt
        {
            get => _updatedAt;
            private set
            {
                if (SetField(ref _updatedAt, value, nameof(UpdatedAt)))
                {
                    OnPropertyChanged(nameof(UpdatedAtDisplay));
                }
            }
        }

        public string UpdatedAtDisplay => UpdatedAt?.ToLocalTime().ToString("G") ?? string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Apply(bool hasUser, bool hasPassword, bool hasTotp)
        {
            HasUser = hasUser;
            HasPassword = hasPassword;
            HasTotp = hasTotp;
            UpdatedAt = DateTime.UtcNow;
        }

        private bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
