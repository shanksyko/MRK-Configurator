using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms;

public sealed class MainForm : Form
{
    private readonly BindingList<SiteConfig> _programs = new();
    private readonly BindingSource _programsSource = new();
    private readonly DataGridView _programGrid;
    private readonly SecretsProvider _secretsProvider;

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Mieruka Configurator";
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var vault = new Mieruka.Core.Security.CredentialVault();
        var cookies = new CookieSafeStore();
        _secretsProvider = new SecretsProvider(vault, cookies);

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
        editButton.Click += (_, _) => OpenEditor();

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
        Controls.Add(programLayout);

        SeedSampleProgram();
    }

    private void OpenEditor()
    {
        using var editor = new AppEditorForm(_secretsProvider);
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
