using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : Form
{
    private readonly BindingList<SiteConfig> _sites = new();

    public AppEditorForm()
    {
        InitializeComponent();
        AcceptButton = btnSalvar;
        CancelButton = btnCancelar;

        sitesEditorControl.Sites = _sites;
        sitesEditorControl.AddRequested += SitesEditorControl_AddRequested;
        sitesEditorControl.RemoveRequested += SitesEditorControl_RemoveRequested;
        sitesEditorControl.CloneRequested += SitesEditorControl_CloneRequested;
        sitesEditorControl.TestarLogin += SitesEditorControl_TestarLogin;
        sitesEditorControl.AplicarPosicao += SitesEditorControl_AplicarPosicao;
    }

    private void SitesEditorControl_AddRequested(object? sender, EventArgs e)
    {
        var site = new SiteConfig
        {
            Id = $"site_{_sites.Count + 1}",
            Url = "https://example.com",
        };

        _sites.Add(site);
        sitesEditorControl.SelectSite(site);
    }

    private void SitesEditorControl_RemoveRequested(object? sender, EventArgs e)
    {
        if (sitesEditorControl.SelectedSite is SiteConfig site)
        {
            _sites.Remove(site);
        }
    }

    private void SitesEditorControl_CloneRequested(object? sender, EventArgs e)
    {
        if (sitesEditorControl.SelectedSite is not SiteConfig current)
        {
            return;
        }

        var clone = new SiteConfig
        {
            Id = $"{current.Id}_clone",
            Url = current.Url,
            KioskMode = current.KioskMode,
            AppMode = current.AppMode,
            AllowedTabHosts = current.AllowedTabHosts?.ToList() ?? new List<string>(),
            BrowserArguments = current.BrowserArguments?.ToList() ?? new List<string>(),
        };

        _sites.Add(clone);
        sitesEditorControl.SelectSite(clone);
    }

    private void SitesEditorControl_TestarLogin(object? sender, string siteId)
    {
        MessageBox.Show(
            this,
            $"Testar login do site '{siteId}' não está disponível nesta versão.",
            "Testar Login",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SitesEditorControl_AplicarPosicao(object? sender, string siteId)
    {
        MessageBox.Show(
            this,
            $"Aplicar posição para o site '{siteId}' não está disponível nesta versão.",
            "Aplicar Posição",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnCancelar_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
