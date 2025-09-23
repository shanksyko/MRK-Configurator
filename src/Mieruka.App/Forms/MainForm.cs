using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms;

public sealed class MainForm : Form
{
    private readonly BindingList<SiteConfig> _programs = new();
    private readonly BindingSource _programsSource = new();
    private readonly DataGridView _programGrid;
    private readonly CredentialVaultPanel _vaultPanel;
    private readonly TabControl _tabs;
    private readonly UiSecretsBridge _secretsBridge;

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Mieruka Configurator";
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var vault = new CredentialVault();
        var cookies = new CookieSafeStore();
        var secretsProvider = new SecretsProvider(vault, cookies);
        _secretsBridge = new UiSecretsBridge(secretsProvider);

        _programsSource.DataSource = _programs;

        _programGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };

        _programGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SiteConfig.Id),
            HeaderText = "SiteId",
            Width = 180,
        });
        _programGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SiteConfig.Url),
            HeaderText = "URL",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _programGrid.DataSource = _programsSource;

        var editButton = new Button
        {
            Text = "Editar…",
            AutoSize = true,
        };
        editButton.Click += (_, _) => OpenEditor(secretsProvider);

        var programLayout = LayoutHelpers.CreateStandardTableLayout();
        programLayout.RowCount = 2;
        programLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        programLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        programLayout.Controls.Add(_programGrid, 0, 0);

        var buttonsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        buttonsPanel.Controls.Add(editButton);
        programLayout.Controls.Add(buttonsPanel, 0, 1);

        var programsTab = new TabPage("Programas")
        {
            Padding = new Padding(8),
        };
        programsTab.Controls.Add(programLayout);

        _vaultPanel = new CredentialVaultPanel(secretsProvider, _secretsBridge)
        {
            ScopeSiteId = null,
        };
        _vaultPanel.OpenGlobalVaultRequested += (_, _) =>
        {
            if (_tabs != null)
            {
                _tabs.SelectedTab = programsTab;
            }
        };

        var vaultTab = new TabPage("CredentialVault")
        {
            Padding = new Padding(8),
        };
        vaultTab.Controls.Add(_vaultPanel);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
        };
        _tabs.TabPages.Add(programsTab);
        _tabs.TabPages.Add(vaultTab);

        Controls.Add(_tabs);

        SeedSampleProgram();
    }

    public void OpenCredentialVault(string siteId)
    {
        if (_vaultPanel is null || _tabs is null)
        {
            return;
        }

        _vaultPanel.ScopeSiteId = siteId;
        _tabs.SelectedIndex = 1;
    }

    private void OpenEditor(SecretsProvider secretsProvider)
    {
        using var editor = new AppEditorForm(secretsProvider);
        editor.ShowDialog(this);
    }

    private void SeedSampleProgram()
    {
        if (_programs.Count > 0)
        {
            return;
        }

        _programs.Add(new SiteConfig
        {
            Id = "sample",
            Url = "https://example.com",
        });
    }
}
