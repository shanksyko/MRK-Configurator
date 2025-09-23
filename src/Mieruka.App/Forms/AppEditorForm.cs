#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : Form
{
    private readonly BindingList<SiteConfig> _sites;
    private readonly ProgramaConfig? _original;
    private readonly List<MonitorInfo> _monitors;
    private readonly string? _preferredMonitorId;
    private MonitorPreviewHost? _monitorPreviewHost;
    private MonitorDescriptor? _selectedMonitorDescriptor;
    private bool _suppressMonitorComboEvents;

    public AppEditorForm(ProgramaConfig? programa = null, IReadOnlyList<MonitorInfo>? monitors = null, string? selectedMonitorId = null)
    {
        InitializeComponent();

        _ = tabEditor ?? throw new InvalidOperationException("O TabControl do editor não foi carregado.");
        var salvar = btnSalvar ?? throw new InvalidOperationException("O botão Salvar não foi carregado.");
        _ = btnCancelar ?? throw new InvalidOperationException("O botão Cancelar não foi carregado.");
        var sitesControl = sitesEditorControl ?? throw new InvalidOperationException("O controle de sites não foi carregado.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi configurado.");

        AcceptButton = salvar;
        CancelButton = btnCancelar;

        _monitors = monitors?.ToList() ?? new List<MonitorInfo>();
        _preferredMonitorId = selectedMonitorId;

        _sites = new BindingList<SiteConfig>();
        sitesControl.Sites = _sites;
        sitesControl.AddRequested += SitesEditorControl_AddRequested;
        sitesControl.RemoveRequested += SitesEditorControl_RemoveRequested;
        sitesControl.CloneRequested += SitesEditorControl_CloneRequested;

        cboMonitores.SelectedIndexChanged += cboMonitores_SelectedIndexChanged;
        PopulateMonitorCombo(programa);

        if (programa is not null)
        {
            _original = programa;
            CarregarPrograma(programa);
        }
        else
        {
            chkAutoStart.Checked = true;
            chkJanelaTelaCheia.Checked = true;
            UpdateMonitorPreview();
        }
    }

    public ProgramaConfig? Resultado { get; private set; }

    public BindingList<SiteConfig> ResultadoSites => new(_sites.Select(site => site with { }).ToList());

    public string? SelectedMonitorId => _selectedMonitorDescriptor?.Id;

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

    private void PopulateMonitorCombo(ProgramaConfig? programa)
    {
        if (cboMonitores is null)
        {
            return;
        }

        _suppressMonitorComboEvents = true;

        try
        {
            cboMonitores.Items.Clear();

            if (_monitors.Count == 0)
            {
                cboMonitores.Items.Add(MonitorOption.Empty());
                cboMonitores.SelectedIndex = 0;
                _selectedMonitorDescriptor = null;
            }
            else
            {
                foreach (var monitor in _monitors)
                {
                    var descriptor = new MonitorDescriptor(monitor, MonitorIdentifier.Create(monitor), LayoutHelpers.GetMonitorDisplayName(monitor));
                    cboMonitores.Items.Add(new MonitorOption(descriptor));
                }

                var candidates = new[]
                {
                    _preferredMonitorId,
                    programa?.TargetMonitorStableId,
                    MonitorIdentifier.Create(programa?.Window?.Monitor),
                };

                var selectionApplied = false;
                foreach (var candidate in candidates)
                {
                    if (SelectMonitorById(candidate))
                    {
                        selectionApplied = true;
                        break;
                    }
                }

                if (!selectionApplied && cboMonitores.Items.Count > 0)
                {
                    cboMonitores.SelectedIndex = 0;
                }
            }
        }
        finally
        {
            _suppressMonitorComboEvents = false;
        }

        UpdateMonitorPreview();
    }

    private bool SelectMonitorById(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || cboMonitores is null)
        {
            return false;
        }

        for (var index = 0; index < cboMonitores.Items.Count; index++)
        {
            if (cboMonitores.Items[index] is not MonitorOption option || option.Descriptor is null)
            {
                continue;
            }

            if (string.Equals(option.Descriptor.Id, identifier, StringComparison.OrdinalIgnoreCase))
            {
                cboMonitores.SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    private void cboMonitores_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressMonitorComboEvents)
        {
            return;
        }

        UpdateMonitorPreview();
    }

    private void UpdateMonitorPreview()
    {
        _monitorPreviewHost?.Dispose();
        _monitorPreviewHost = null;
        _selectedMonitorDescriptor = null;

        if (cboMonitores?.SelectedItem is not MonitorOption option || option.Descriptor is null || picMonitorPreview is null)
        {
            if (picMonitorPreview is not null)
            {
                picMonitorPreview.Image = null;
            }

            return;
        }

        _selectedMonitorDescriptor = option.Descriptor;
        _monitorPreviewHost = new MonitorPreviewHost(option.Descriptor, picMonitorPreview, preferGpu: true);
        _monitorPreviewHost.Start();
    }

    private MonitorDescriptor? GetSelectedMonitorDescriptor()
    {
        if (_selectedMonitorDescriptor is not null)
        {
            return _selectedMonitorDescriptor;
        }

        return cboMonitores?.SelectedItem is MonitorOption option ? option.Descriptor : null;
    }

    private static WindowConfig ClampWindowBounds(WindowConfig window, MonitorInfo monitor)
    {
        var width = window.Width;
        var height = window.Height;
        var x = window.X;
        var y = window.Y;

        if (width is int w && monitor.Width > 0)
        {
            width = Math.Clamp(w, 1, monitor.Width);
        }

        if (height is int h && monitor.Height > 0)
        {
            height = Math.Clamp(h, 1, monitor.Height);
        }

        if (x is int posX && width is int wValue && monitor.Width > 0)
        {
            var maxX = Math.Max(0, monitor.Width - wValue);
            x = Math.Clamp(posX, 0, maxX);
        }

        if (y is int posY && height is int hValue && monitor.Height > 0)
        {
            var maxY = Math.Max(0, monitor.Height - hValue);
            y = Math.Clamp(posY, 0, maxY);
        }

        return window with
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };
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

        var monitorDescriptor = GetSelectedMonitorDescriptor();
        if (monitorDescriptor is not null)
        {
            if (!janela.FullScreen)
            {
                janela = ClampWindowBounds(janela, monitorDescriptor.Monitor);
            }

            janela = janela with { Monitor = monitorDescriptor.Monitor.Key };
        }

        return (_original ?? new ProgramaConfig()) with
        {
            Id = id,
            ExecutablePath = executavel,
            Arguments = argumentos,
            AutoStart = chkAutoStart.Checked,
            Window = janela,
            TargetMonitorStableId = monitorDescriptor?.Monitor.StableId ?? string.Empty,
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _monitorPreviewHost?.Dispose();
        _monitorPreviewHost = null;
    }

    private sealed class MonitorOption
    {
        public MonitorOption(MonitorDescriptor? descriptor, string displayName)
        {
            Descriptor = descriptor;
            DisplayName = displayName;
        }

        public MonitorOption(MonitorDescriptor descriptor)
            : this(descriptor, descriptor.DisplayName)
        {
        }

        public MonitorDescriptor? Descriptor { get; }

        public string DisplayName { get; }

        public static MonitorOption Empty()
            => new(null, "Nenhum monitor disponível");

        public override string ToString() => DisplayName;
    }
}
