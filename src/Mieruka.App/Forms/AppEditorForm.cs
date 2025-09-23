#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : Form
{
    private readonly BindingList<SiteConfig> _sites;
    private readonly ProgramaConfig? _original;

    public AppEditorForm(ProgramaConfig? programa = null)
    {
        InitializeComponent();

        _ = tabEditor ?? throw new InvalidOperationException("O TabControl do editor não foi carregado.");
        var salvar = btnSalvar ?? throw new InvalidOperationException("O botão Salvar não foi carregado.");
        _ = btnCancelar ?? throw new InvalidOperationException("O botão Cancelar não foi carregado.");
        var sitesControl = sitesEditorControl ?? throw new InvalidOperationException("O controle de sites não foi carregado.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi configurado.");

        AcceptButton = salvar;
        CancelButton = btnCancelar;

        _sites = new BindingList<SiteConfig>();
        sitesControl.Sites = _sites;
        sitesControl.AddRequested += SitesEditorControl_AddRequested;
        sitesControl.RemoveRequested += SitesEditorControl_RemoveRequested;
        sitesControl.CloneRequested += SitesEditorControl_CloneRequested;

        if (programa is not null)
        {
            _original = programa;
            CarregarPrograma(programa);
        }
        else
        {
            chkAutoStart.Checked = true;
            chkJanelaTelaCheia.Checked = true;
        }
    }

    public ProgramaConfig? Resultado { get; private set; }

    public BindingList<SiteConfig> ResultadoSites => new(_sites.Select(site => site with { }).ToList());

    private void CarregarPrograma(ProgramaConfig programa)
    {
        txtId.Text = programa.Id;
        txtExecutavel.Text = programa.ExecutablePath;
        txtArgumentos.Text = programa.Arguments ?? string.Empty;
        chkAutoStart.Checked = programa.AutoStart;

        var janela = programa.Window ?? new WindowConfig();
        chkJanelaTelaCheia.Checked = janela.FullScreen;
        if (!janela.FullScreen)
        {
            if (janela.X is int x)
            {
                nudJanelaX.Value = AjustarRange(nudJanelaX, x);
            }
            if (janela.Y is int y)
            {
                nudJanelaY.Value = AjustarRange(nudJanelaY, y);
            }
            if (janela.Width is int largura)
            {
                nudJanelaLargura.Value = AjustarRange(nudJanelaLargura, largura);
            }
            if (janela.Height is int altura)
            {
                nudJanelaAltura.Value = AjustarRange(nudJanelaAltura, altura);
            }
        }
    }

    private static decimal AjustarRange(NumericUpDown control, int value)
    {
        var decimalValue = (decimal)value;
        if (decimalValue < control.Minimum)
        {
            return control.Minimum;
        }

        if (decimalValue > control.Maximum)
        {
            return control.Maximum;
        }

        return decimalValue;
    }

    private void SitesEditorControl_AddRequested(object? sender, EventArgs e)
    {
        var novoId = GerarIdSite();
        var site = new SiteConfig
        {
            Id = novoId,
            Url = "https://exemplo.com",
        };

        _sites.Add(site);
        sitesEditorControl?.SelectSite(site);
    }

    private void SitesEditorControl_RemoveRequested(object? sender, EventArgs e)
    {
        var site = sitesEditorControl?.SelectedSite;
        if (site is null)
        {
            return;
        }

        _sites.Remove(site);
    }

    private void SitesEditorControl_CloneRequested(object? sender, EventArgs e)
    {
        var site = sitesEditorControl?.SelectedSite;
        if (site is null)
        {
            return;
        }

        var clone = site with { Id = GerarIdSite(site.Id + "_clone") };
        _sites.Add(clone);
        sitesEditorControl?.SelectSite(clone);
    }

    private string GerarIdSite(string? baseId = null)
    {
        var prefixo = string.IsNullOrWhiteSpace(baseId) ? "site" : baseId;
        var contador = 1;

        var candidato = prefixo;
        while (_sites.Any(s => string.Equals(s.Id, candidato, StringComparison.OrdinalIgnoreCase)))
        {
            candidato = $"{prefixo}_{contador++}";
        }

        return candidato;
    }

    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        if (!ValidarCampos())
        {
            DialogResult = DialogResult.None;
            return;
        }

        Resultado = ConstruirPrograma();
        DialogResult = DialogResult.OK;
        Close();
    }

    private ProgramaConfig ConstruirPrograma()
    {
        var id = txtId.Text.Trim();
        var executavel = txtExecutavel.Text.Trim();
        var argumentos = string.IsNullOrWhiteSpace(txtArgumentos.Text) ? null : txtArgumentos.Text.Trim();

        var janela = chkJanelaTelaCheia.Checked
            ? new WindowConfig { FullScreen = true }
            : new WindowConfig
            {
                FullScreen = false,
                X = (int)nudJanelaX.Value,
                Y = (int)nudJanelaY.Value,
                Width = (int)nudJanelaLargura.Value,
                Height = (int)nudJanelaAltura.Value,
            };

        return (_original ?? new ProgramaConfig()) with
        {
            Id = id,
            ExecutablePath = executavel,
            Arguments = argumentos,
            AutoStart = chkAutoStart.Checked,
            Window = janela,
        };
    }

    private bool ValidarCampos()
    {
        var valido = true;

        if (string.IsNullOrWhiteSpace(txtId.Text))
        {
            errorProvider.SetError(txtId, "Informe um identificador." );
            valido = false;
        }
        else
        {
            errorProvider.SetError(txtId, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(txtExecutavel.Text))
        {
            errorProvider.SetError(txtExecutavel, "Informe o executável." );
            valido = false;
        }
        else
        {
            errorProvider.SetError(txtExecutavel, string.Empty);
        }

        return valido;
    }

    private void btnCancelar_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
