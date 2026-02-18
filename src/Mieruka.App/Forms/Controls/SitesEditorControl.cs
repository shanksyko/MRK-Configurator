using System;
using System.ComponentModel;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls.Sites;
using Mieruka.Core.Models;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Forms.Controls;

public partial class SitesEditorControl : WinForms.UserControl
{
    private BindingList<SiteConfig> _sites = new();
    private SiteConfig? _selectedSite;

    public SitesEditorControl()
    {
        InitializeComponent();

        _ = bsSites ?? throw new InvalidOperationException("BindingSource não inicializado.");
        _ = dgvSites ?? throw new InvalidOperationException("DataGridView não inicializado.");
        _ = siteConfigTab ?? throw new InvalidOperationException("SiteConfigTab não foi criado.");
        _ = loginAutoTab ?? throw new InvalidOperationException("LoginAutoTab não foi criado.");
        _ = whitelistTab ?? throw new InvalidOperationException("WhitelistTab não foi criado.");
        _ = argsTab ?? throw new InvalidOperationException("ArgsTab não foi criado.");
        _ = credentialVaultPanel ?? throw new InvalidOperationException("CredentialVaultPanel não foi criado.");

        bsSites.DataSource = _sites;

        loginAutoTab.TestLoginRequested += (_, site) =>
        {
            if (!string.IsNullOrEmpty(site))
            {
                TestarLogin?.Invoke(this, site);
            }
        };
        loginAutoTab.ApplyPositionRequested += (_, site) =>
        {
            if (!string.IsNullOrEmpty(site))
            {
                AplicarPosicao?.Invoke(this, site);
            }
        };
        credentialVaultPanel.TestLoginRequested += (_, site) =>
        {
            if (!string.IsNullOrEmpty(site))
            {
                TestarLogin?.Invoke(this, site);
            }
        };

        UpdateSelection();
    }

    public event EventHandler? AddRequested;
    public event EventHandler? RemoveRequested;
    public event EventHandler? CloneRequested;
    public event EventHandler<string>? TestarLogin;
    public event EventHandler<string>? AplicarPosicao;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BindingList<SiteConfig> Sites
    {
        get => _sites;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!ReferenceEquals(_sites, value))
            {
                _sites = value;
                bsSites.DataSource = _sites;
            }

            UpdateSelection();
        }
    }

    public SiteConfig? SelectedSite => _selectedSite;

    public void SelectSite(SiteConfig site)
    {
        var list = Sites;
        var index = list.IndexOf(site);
        if (index < 0)
        {
            return;
        }

        if (index < dgvSites.Rows.Count)
        {
            dgvSites.ClearSelection();
            var row = dgvSites.Rows[index];
            row.Selected = true;
            dgvSites.CurrentCell = row.Cells[0];
        }
    }

    private void btnAdicionarSite_Click(object? sender, EventArgs e)
    {
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    private void btnRemoverSite_Click(object? sender, EventArgs e)
    {
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void btnClonarSite_Click(object? sender, EventArgs e)
    {
        CloneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void btnTestarSite_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedSite?.Id))
        {
            TestarLogin?.Invoke(this, _selectedSite!.Id);
        }
    }

    private void btnAplicarPosicao_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedSite?.Id))
        {
            AplicarPosicao?.Invoke(this, _selectedSite!.Id);
        }
    }

    private void dgvSites_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (bsSites.Current is SiteConfig site)
        {
            _selectedSite = site;
        }
        else
        {
            _selectedSite = null;
        }

        siteConfigTab.Bind(_selectedSite);
        loginAutoTab.BindSite(_selectedSite);
        whitelistTab.BindSite(_selectedSite);
        argsTab.BindSite(_selectedSite);
        credentialVaultPanel.ScopeSiteId = _selectedSite?.Id;

        var hasSelection = _selectedSite is not null;
        btnRemoverSite.Enabled = hasSelection;
        btnClonarSite.Enabled = hasSelection;
        btnTestarSite.Enabled = hasSelection;
        btnAplicarPosicao.Enabled = hasSelection;
    }
}
