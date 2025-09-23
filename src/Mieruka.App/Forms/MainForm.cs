#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Ui;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class MainForm : Form
{
    private readonly BindingList<ProgramaConfig> _programas = new();
    private readonly ITelemetry _telemetry = new UiTelemetry();
    private readonly Orchestrator _orchestrator;
    private bool _busy;

    public MainForm()
    {
        InitializeComponent();

        var grid = dgvProgramas ?? throw new InvalidOperationException("O DataGridView de programas não foi criado pelo designer.");
        var source = bsProgramas ?? throw new InvalidOperationException("A BindingSource de programas não foi inicializada.");
        _ = menuPreview ?? throw new InvalidOperationException("O menu de preview não foi criado.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi criado.");

        source.DataSource = _programas;
        grid.AutoGenerateColumns = false;
        grid.DataSource = source;

        grid.SelectionChanged += (_, _) => UpdateButtonStates();
        grid.CellDoubleClick += (_, _) => btnEditar_Click(this, EventArgs.Empty);
        grid.KeyDown += dgvProgramas_KeyDown;

        _orchestrator = CreateOrchestrator();
        _orchestrator.StateChanged += Orchestrator_StateChanged;

        LoadInitialData();
        UpdateButtonStates();
    }

    private void LoadInitialData()
    {
        if (_programas.Count > 0)
        {
            return;
        }

        _programas.Add(new ProgramaConfig
        {
            Id = "app_principal",
            ExecutablePath = @"C:\\Program Files\\Mieruka\\MierukaPlayer.exe",
            AutoStart = true,
            TargetMonitorStableId = string.Empty,
        });
    }

    private Orchestrator CreateOrchestrator()
    {
        var component = new NoOpComponent();
        return new Orchestrator(component, component, component, component, _telemetry);
    }

    private void dgvProgramas_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateButtonStates();
    }

    private void dgvProgramas_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            btnEditar_Click(sender, EventArgs.Empty);
        }
    }

    private void dgvProgramas_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            btnExcluir_Click(sender, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void menuPreview_Click(object? sender, EventArgs e)
    {
        try
        {
            var preview = new PreviewForm();
            preview.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o preview: {ex.Message}", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnAdicionar_Click(object? sender, EventArgs e)
    {
        AbrirEditor(null);
    }

    private void btnEditar_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        AbrirEditor(selecionado);
    }

    private void btnDuplicar_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        var novoId = GerarIdentificadorUnico(selecionado.Id + "_copia");
        var copia = selecionado with { Id = novoId };
        _programas.Add(copia);
        SelecionarPrograma(copia);
    }

    private void btnExcluir_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        var confirmacao = MessageBox.Show(
            this,
            $"Deseja remover o programa '{selecionado.Id}'?",
            "Excluir Programa",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmacao != DialogResult.Yes)
        {
            return;
        }

        _programas.Remove(selecionado);
    }

    private async void btnExecutar_Click(object? sender, EventArgs e)
    {
        await ExecutarOrchestratorAsync().ConfigureAwait(false);
    }

    private async void btnParar_Click(object? sender, EventArgs e)
    {
        await PararOrchestratorAsync().ConfigureAwait(false);
    }

    private async Task ExecutarOrchestratorAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtonStates();

        try
        {
            await _orchestrator.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao iniciar o orchestrator.", ex);
            MessageBox.Show(this, $"Erro ao iniciar: {ex.Message}", "Orchestrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateButtonStates();
        }
    }

    private async Task PararOrchestratorAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtonStates();

        try
        {
            await _orchestrator.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao parar o orchestrator.", ex);
            MessageBox.Show(this, $"Erro ao parar: {ex.Message}", "Orchestrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateButtonStates();
        }
    }

    private void Orchestrator_StateChanged(object? sender, OrchestratorStateChangedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(new MethodInvoker(UpdateButtonStates));
        }
        catch (ObjectDisposedException)
        {
            // Ignore because the form is closing.
        }
    }

    private void UpdateButtonStates()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new MethodInvoker(UpdateButtonStates));
            return;
        }

        var selecionado = ObterProgramaSelecionado();
        var temSelecao = selecionado is not null;
        var estado = _orchestrator.State;
        var podeExecutar = !_busy && estado is not OrchestratorState.Running and not OrchestratorState.Recovering;
        var podeParar = !_busy && estado is OrchestratorState.Running or OrchestratorState.Recovering;

        btnEditar.Enabled = temSelecao;
        btnDuplicar.Enabled = temSelecao;
        btnExcluir.Enabled = temSelecao;
        btnExecutar.Enabled = podeExecutar;
        btnParar.Enabled = podeParar;
    }

    private ProgramaConfig? ObterProgramaSelecionado()
    {
        return bsProgramas?.Current as ProgramaConfig;
    }

    private void SelecionarPrograma(ProgramaConfig programa)
    {
        var grid = dgvProgramas;
        if (grid is null)
        {
            return;
        }

        for (var i = 0; i < grid.Rows.Count; i++)
        {
            var row = grid.Rows[i];
            if (row.DataBoundItem is ProgramaConfig atual && ReferenceEquals(atual, programa))
            {
                grid.ClearSelection();
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private string GerarIdentificadorUnico(string baseId)
    {
        var id = baseId;
        var contador = 1;
        while (_programas.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}_{contador++}";
        }

        return id;
    }

    private void AbrirEditor(ProgramaConfig? selected)
    {
        using var editor = new AppEditorForm(selected);
        var resultado = editor.ShowDialog(this);
        if (resultado != DialogResult.OK)
        {
            return;
        }

        var programa = editor.Resultado;
        if (programa is null)
        {
            return;
        }

        if (selected is null)
        {
            _programas.Add(programa);
            SelecionarPrograma(programa);
            return;
        }

        var indice = _programas.IndexOf(selected);
        if (indice >= 0)
        {
            _programas[indice] = programa;
            bsProgramas.ResetBindings(false);
            SelecionarPrograma(programa);
        }
    }

    private sealed class NoOpComponent : IOrchestrationComponent
    {
        public Task StartAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecoverAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class UiTelemetry : ITelemetry
    {
        public void Info(string message, Exception? exception = null)
        {
            Debug.WriteLine($"[INFO] {message} {exception?.Message}");
        }

        public void Warn(string message, Exception? exception = null)
        {
            Debug.WriteLine($"[WARN] {message} {exception?.Message}");
        }

        public void Error(string message, Exception? exception = null)
        {
            Debug.WriteLine($"[ERROR] {message} {exception?.Message}");
        }
    }
}
