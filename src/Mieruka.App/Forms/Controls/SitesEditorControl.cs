using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls.Sites;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms.Controls;

public sealed class SitesEditorControl : UserControl
{
    private readonly BindingList<SiteConfig> _sites = new();
    private readonly BindingSource _source = new();
    private readonly DataGridView _grid;
    private readonly CredentialVaultPanel _vaultPanel;
    private readonly SiteConfigTab _configTab;
    private readonly LoginAutoTab _loginTab;
    private readonly WhitelistTab _whitelistTab;
    private readonly ArgsTab _argsTab;
    private readonly SplitContainer _split;
    private SiteConfig? _selectedSite;

    public event EventHandler? AddRequested;
    public event EventHandler? RemoveRequested;
    public event EventHandler? CloneRequested;
    public event EventHandler<string>? TestarLogin;
    public event EventHandler<string>? AplicarPosicao;

    public SitesEditorControl(SecretsProvider secretsProvider)
    {
        LayoutHelpers.ApplyStandardLayout(this);

        var bridge = new UiSecretsBridge(secretsProvider);
        _vaultPanel = new CredentialVaultPanel(secretsProvider, bridge)
        {
            ScopeSiteId = null,
        };
        _vaultPanel.TestLoginRequested += (_, site) => TestarLogin?.Invoke(this, site);
        _vaultPanel.OpenGlobalVaultRequested += (_, _) => _vaultPanel.ScopeSiteId = null;

        _configTab = new SiteConfigTab();
        _loginTab = new LoginAutoTab(secretsProvider);
        _loginTab.TestLoginRequested += (_, site) => TestarLogin?.Invoke(this, site);
        _loginTab.ApplyPositionRequested += (_, site) => AplicarPosicao?.Invoke(this, site);
        _whitelistTab = new WhitelistTab();
        _argsTab = new ArgsTab();

        _source.DataSource = _sites;
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ReadOnly = true,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SiteConfig.Id),
            HeaderText = "SiteId",
            Width = 160,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SiteConfig.Url),
            HeaderText = "URL",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _grid.DataSource = _source;
        _grid.SelectionChanged += (_, _) => UpdateSelectedSite();

        var addButton = new Button { Text = "Adicionar", AutoSize = true };
        addButton.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        var removeButton = new Button { Text = "Remover", AutoSize = true };
        removeButton.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
        var cloneButton = new Button { Text = "Clonar", AutoSize = true };
        cloneButton.Click += (_, _) => CloneRequested?.Invoke(this, EventArgs.Empty);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        toolbar.Controls.Add(addButton);
        toolbar.Controls.Add(removeButton);
        toolbar.Controls.Add(cloneButton);

        var group = new GroupBox
        {
            Text = "Sites do Programa",
            Dock = DockStyle.Fill,
        };
        var groupLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        groupLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        groupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        groupLayout.Controls.Add(toolbar, 0, 0);
        groupLayout.Controls.Add(_grid, 0, 1);
        group.Controls.Add(groupLayout);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        _split.Panel1.Controls.Add(group);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
        };
        tabs.TabPages.Add(CreateTab("Config", _configTab));
        tabs.TabPages.Add(CreateTab("Login Automático", _loginTab));
        tabs.TabPages.Add(CreateTab("Whitelist", _whitelistTab));
        tabs.TabPages.Add(CreateTab("Args", _argsTab));
        tabs.TabPages.Add(CreateTab("CredentialVault", _vaultPanel));

        _split.Panel2.Controls.Add(tabs);
        Controls.Add(_split);
    }

    public BindingList<SiteConfig> Sites => _sites;

    public SiteConfig? SelectedSite => _selectedSite;

    private static TabPage CreateTab(string title, Control control)
    {
        var tab = new TabPage(title)
        {
            Padding = new Padding(8),
        };
        control.Dock = DockStyle.Fill;
        tab.Controls.Add(control);
        return tab;
    }

    private void UpdateSelectedSite()
    {
        if (_grid.CurrentRow?.DataBoundItem is SiteConfig site)
        {
            _selectedSite = site;
        }
        else
        {
            _selectedSite = null;
        }

        _configTab.Bind(site: _selectedSite);
        _loginTab.BindSite(_selectedSite);
        _whitelistTab.BindSite(_selectedSite);
        _argsTab.BindSite(_selectedSite);
        _vaultPanel.ScopeSiteId = _selectedSite?.Id;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_split.Width > 0)
        {
            _split.SplitterDistance = (int)Math.Max(200, _split.Width * 0.4);
        }
    }
}
