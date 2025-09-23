using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms;

public partial class MainForm : Form
{
    private readonly BindingList<SiteConfig> _sites = new();

    public MainForm()
    {
        InitializeComponent();

        bsSites.DataSource = _sites;
        dgvSites.DataSource = bsSites;
        dgvSites.SelectionChanged += (_, _) => UpdateButtons();
        dgvSites.CellDoubleClick += (_, _) => btnEditar_Click(this, EventArgs.Empty);
        dgvSites.KeyDown += dgvSites_KeyDown;

        SeedSampleData();
        UpdateButtons();
    }

    private void btnAdicionar_Click(object? sender, EventArgs e)
    {
        var site = new SiteConfig
        {
            Id = $"site_{_sites.Count + 1}",
            Url = "https://example.com",
        };

        _sites.Add(site);
        SelectSite(site);
    }

    private void btnEditar_Click(object? sender, EventArgs e)
    {
        using var editor = new AppEditorForm();
        editor.ShowDialog(this);
    }

    private void btnClonar_Click(object? sender, EventArgs e)
    {
        if (bsSites.Current is not SiteConfig current)
        {
            return;
        }

        var clone = new SiteConfig
        {
            Id = $"{current.Id}_clone",
            Url = current.Url,
            KioskMode = current.KioskMode,
            AppMode = current.AppMode,
            AllowedTabHosts = current.AllowedTabHosts?.ToList(),
            BrowserArguments = current.BrowserArguments?.ToList(),
        };

        _sites.Add(clone);
        SelectSite(clone);
    }

    private void btnRemover_Click(object? sender, EventArgs e)
    {
        if (bsSites.Current is not SiteConfig current)
        {
            return;
        }

        _sites.Remove(current);
    }

    private void btnTestar_Click(object? sender, EventArgs e)
    {
        if (bsSites.Current is not SiteConfig current)
        {
            return;
        }

        MessageBox.Show(
            this,
            $"Teste de login para o site \"{current.Id}\" não está disponível nesta compilação.",
            "Testar Login",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void dgvSites_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            btnRemover_Click(sender, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void SeedSampleData()
    {
        if (_sites.Count > 0)
        {
            return;
        }

        _sites.Add(new SiteConfig
        {
            Id = "sample",
            Url = "https://example.com",
        });
    }

    private void SelectSite(SiteConfig site)
    {
        var index = _sites.IndexOf(site);
        if (index >= 0 && index < dgvSites.Rows.Count)
        {
            dgvSites.ClearSelection();
            var row = dgvSites.Rows[index];
            row.Selected = true;
            dgvSites.CurrentCell = row.Cells[0];
        }
    }

    private void UpdateButtons()
    {
        var hasSelection = bsSites.Current is SiteConfig;
        btnEditar.Enabled = hasSelection;
        btnClonar.Enabled = hasSelection;
        btnRemover.Enabled = hasSelection;
        btnTestar.Enabled = hasSelection;
    }
}
