using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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

        // Wire SecretsProvider from CredentialVaultPanel into LoginAutoTab.
        loginAutoTab.SetSecretsProvider(credentialVaultPanel.Secrets);

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

    /// <summary>
    /// Commits any pending edits from the tab controls back into the selected site's entry in the list.
    /// Called automatically before switching sites and can be called externally before saving.
    /// </summary>
    public void CommitCurrentSiteEdits()
    {
        if (_selectedSite is null)
        {
            return;
        }

        var index = _sites.IndexOf(_selectedSite);
        if (index < 0)
        {
            return;
        }

        var login = loginAutoTab.CollectLoginProfile();
        var args = argsTab.CollectArgs();

        // Merge timeout from ArgsTab into the login profile.
        if (login is not null)
        {
            login = login with { TimeoutSeconds = args.Timeout };
        }

        var extraArgs = new List<string>(_selectedSite.BrowserArguments ?? Array.Empty<string>());

        // Remove args that are managed by ArgsTab to avoid duplicates.
        extraArgs.RemoveAll(a =>
            a.StartsWith("--kiosk", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--app", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--incognito", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--proxy-server", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--proxy-bypass-list", StringComparison.OrdinalIgnoreCase));

        if (args.Incognito)
        {
            extraArgs.Add("--incognito");
        }

        if (!string.IsNullOrWhiteSpace(args.Proxy))
        {
            extraArgs.Add($"--proxy-server=\"{args.Proxy}\"");
        }

        if (!string.IsNullOrWhiteSpace(args.ProxyBypass))
        {
            extraArgs.Add($"--proxy-bypass-list=\"{args.ProxyBypass}\"");
        }

        var updated = _selectedSite with
        {
            Login = login,
            KioskMode = args.Kiosk,
            AppMode = args.AppMode,
            BrowserArguments = extraArgs,
        };

        _sites[index] = updated;
        _selectedSite = updated;
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
        // Commit edits from the previous site before switching.
        CommitCurrentSiteEdits();
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
