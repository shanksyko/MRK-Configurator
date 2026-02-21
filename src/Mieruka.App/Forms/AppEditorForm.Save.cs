#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.Core.Config;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        if (!ValidarCampos())
        {
            DialogResult = WinForms.DialogResult.None;
            return;
        }

        Resultado = ConstruirPrograma();
        CommitProfileMetadata();
        DialogResult = WinForms.DialogResult.OK;
        Close();
    }

    private ProgramaConfig ConstruirPrograma()
    {
        var id = txtId.Text.Trim();
        var executavel = txtExecutavel.Text.Trim();
        var argumentos = string.IsNullOrWhiteSpace(txtArgumentos.Text) ? null : txtArgumentos.Text.Trim();

        var janela = chkJanelaTelaCheia.Checked
            ? new WindowConfig
            {
                FullScreen = true,
                Title = txtWindowTitle.Text.Trim(),
                AlwaysOnTop = chkAlwaysOnTop.Checked,
            }
            : new WindowConfig
            {
                FullScreen = false,
                X = (int)nudJanelaX.Value,
                Y = (int)nudJanelaY.Value,
                Width = (int)nudJanelaLargura.Value,
                Height = (int)nudJanelaAltura.Value,
                Title = txtWindowTitle.Text.Trim(),
                AlwaysOnTop = chkAlwaysOnTop.Checked,
            };

        var monitorInfo = GetSelectedMonitor();
        if (monitorInfo is not null)
        {
            if (!janela.FullScreen)
            {
                janela = ClampWindowBounds(janela, monitorInfo);
            }

            janela = janela with { Monitor = monitorInfo.Key };
        }

        if (_editingMetadata is not null)
        {
            _editingMetadata.Id = id;
        }

        return (_original ?? new ProgramaConfig()) with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(txtNomeAmigavel.Text) ? null : txtNomeAmigavel.Text.Trim(),
            ExecutablePath = executavel,
            Arguments = argumentos,
            AutoStart = chkAutoStart.Checked,
            Window = janela,
            TargetMonitorStableId = monitorInfo?.StableId ?? string.Empty,
            Order = _editingMetadata?.Order ?? 0,
            DelayMs = _editingMetadata?.DelayMs ?? 0,
            AskBeforeLaunch = _editingMetadata?.AskBeforeLaunch ?? false,
            RequiresNetwork = _editingMetadata?.RequiresNetwork
                ?? _original?.RequiresNetwork
                ?? false,
            Watchdog = new WatchdogSettings
            {
                Enabled = chkWatchdogEnabled.Checked,
                RestartGracePeriodSeconds = (int)nudWatchdogGrace.Value,
                HealthCheck = BuildHealthCheckConfig(),
            },
            EnvironmentVariables = ParseEnvironmentVariables(),
        };
    }

    private IReadOnlyDictionary<string, string> ParseEnvironmentVariables()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(txtEnvVars.Text)) return dict;

        foreach (var line in txtEnvVars.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0) continue;
            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
            {
                dict[key] = value;
            }
        }

        return dict;
    }

    private HealthCheckConfig? BuildHealthCheckConfig()
    {
        var type = (HealthCheckKind)cmbHealthCheckType.SelectedIndex;
        if (type == HealthCheckKind.None)
            return null;

        return new HealthCheckConfig
        {
            Type = type,
            Url = string.IsNullOrWhiteSpace(txtHealthCheckUrl.Text) ? null : txtHealthCheckUrl.Text.Trim(),
            DomSelector = string.IsNullOrWhiteSpace(txtHealthCheckDomSelector.Text) ? null : txtHealthCheckDomSelector.Text.Trim(),
            ContainsText = string.IsNullOrWhiteSpace(txtHealthCheckContainsText.Text) ? null : txtHealthCheckContainsText.Text.Trim(),
            IntervalSeconds = (int)nudHealthCheckInterval.Value,
            TimeoutSeconds = (int)nudHealthCheckTimeout.Value,
        };
    }

    private void UpdateHealthCheckFieldsVisibility()
    {
        var type = (HealthCheckKind)cmbHealthCheckType.SelectedIndex;
        var showPing = type == HealthCheckKind.Ping || type == HealthCheckKind.Dom;
        var showDom = type == HealthCheckKind.Dom;

        txtHealthCheckUrl.Visible = showPing;
        txtHealthCheckUrl.Parent!.GetContainerControl()?.ToString(); // force layout
        nudHealthCheckInterval.Visible = showPing;
        nudHealthCheckTimeout.Visible = showPing;

        txtHealthCheckDomSelector.Visible = showDom;
        txtHealthCheckContainsText.Visible = showDom;

        // Also toggle labels
        foreach (Control ctrl in txtHealthCheckUrl.Parent!.Controls)
        {
            if (ctrl is Label lbl)
            {
                if (lbl.Name == "lblHealthCheckUrl" || lbl.Name == "lblHealthCheckInterval" || lbl.Name == "lblHealthCheckTimeout")
                    lbl.Visible = showPing;
                if (lbl.Name == "lblHealthCheckDomSelector" || lbl.Name == "lblHealthCheckContainsText")
                    lbl.Visible = showDom;
            }
        }
    }

    private bool ValidarCampos()
    {
        var valido = true;

        if (string.IsNullOrWhiteSpace(txtId.Text))
        {
            errorProvider.SetError(txtId, "Informe um identificador.");
            valido = false;
        }
        else
        {
            errorProvider.SetError(txtId, string.Empty);
        }

        var validarExecutavel = rbExe?.Checked ?? true;
        if (validarExecutavel)
        {
            if (string.IsNullOrWhiteSpace(txtExecutavel.Text))
            {
                errorProvider.SetError(txtExecutavel, "Informe o executável.");
                valido = false;
            }
            else if (!File.Exists(txtExecutavel.Text))
            {
                errorProvider.SetError(txtExecutavel, "Executável não encontrado.");
                valido = false;
            }
            else
            {
                errorProvider.SetError(txtExecutavel, string.Empty);
            }
        }
        else
        {
            errorProvider.SetError(txtExecutavel, string.Empty);

            // Validação modo navegador
            if (_sites.Count == 0)
            {
                errorProvider.SetError(sitesEditorControl, "Adicione pelo menos um site.");
                valido = false;
            }
            else
            {
                errorProvider.SetError(sitesEditorControl, string.Empty);
            }

            if (cmbBrowserEngine.SelectedIndex < 0)
            {
                errorProvider.SetError(cmbBrowserEngine, "Selecione um motor de navegador.");
                valido = false;
            }
            else
            {
                errorProvider.SetError(cmbBrowserEngine, string.Empty);
            }
        }

        return valido;
    }

    private void ValidarCampoId()
    {
        if (string.IsNullOrWhiteSpace(txtId.Text))
        {
            errorProvider.SetError(txtId, "Informe um identificador.");
        }
        else
        {
            errorProvider.SetError(txtId, string.Empty);
        }
    }

    private void btnCancelar_Click(object? sender, EventArgs e)
    {
        DialogResult = WinForms.DialogResult.Cancel;
        Close();
    }

    protected override bool ProcessCmdKey(ref WinForms.Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                btnSalvar_Click(this, EventArgs.Empty);
                return true;
            case Keys.Control | Keys.D1:
                tabEditor.SelectedIndex = 0;
                return true;
            case Keys.Control | Keys.D2:
                if (tabEditor.TabCount > 1) tabEditor.SelectedIndex = 1;
                return true;
            case Keys.Control | Keys.D3:
                if (tabEditor.TabCount > 2) tabEditor.SelectedIndex = 2;
                return true;
            case Keys.Control | Keys.D4:
                if (tabEditor.TabCount > 3) tabEditor.SelectedIndex = 3;
                return true;
            case Keys.Control | Keys.D5:
                if (tabEditor.TabCount > 4) tabEditor.SelectedIndex = 4;
                return true;
            case Keys.Control | Keys.D6:
                if (tabEditor.TabCount > 5) tabEditor.SelectedIndex = 5;
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override async void OnFormClosing(WinForms.FormClosingEventArgs e)
    {
        if (DialogResult != WinForms.DialogResult.OK && _isDirty)
        {
            var result = MessageBox.Show(
                this,
                "Existem alterações não salvas. Deseja descartar?",
                "Confirmação",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != WinForms.DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        StopCycleSimulation();
        base.OnFormClosing(e);

        if (monitorPreviewDisplay is { } preview)
        {
            try
            {
                await preview.StopPreviewAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Falha ao encerrar pré-visualização ao fechar o editor.");
            }
        }
    }

    private void AppsTab_ExecutableChosen(object? sender, AppSelectionEventArgs e)
    {
        if (_suppressListSelectionChanged)
        {
            return;
        }

        if (e.App is null)
        {
            _execSourceMode = ExecSourceMode.Custom;
            txtExecutavel.Text = e.ExecutablePath;
            appsTabControl!.ExecutablePath = e.ExecutablePath;
            UpdateExePreview();

            if (ClearAppsInventorySelectionMethod is not null)
            {
                try
                {
                    _suppressListSelectionChanged = true;
                    ClearAppsInventorySelectionMethod.Invoke(appsTabControl, Array.Empty<object>());
                }
                catch (System.Reflection.TargetInvocationException)
                {
                    // Ignored: falha ao limpar a seleção não deve impedir a seleção personalizada.
                }
                finally
                {
                    _suppressListSelectionChanged = false;
                }
            }

            return;
        }

        if (_execSourceMode == ExecSourceMode.Custom)
        {
            return;
        }

        _execSourceMode = ExecSourceMode.Inventory;
        txtExecutavel.Text = e.ExecutablePath;
        appsTabControl!.ExecutablePath = e.ExecutablePath;
        UpdateExePreview();
    }

    private void AppsTab_ExecutableCleared(object? sender, EventArgs e)
    {
        if (_suppressListSelectionChanged)
        {
            return;
        }

        _execSourceMode = ExecSourceMode.None;
        txtExecutavel.Text = string.Empty;
        appsTabControl!.ExecutablePath = string.Empty;
        UpdateExePreview();
    }

    private void AppsTab_ArgumentsChanged(object? sender, string e)
    {
        txtArgumentos.Text = e;
        UpdateExePreview();
    }

    private void UpdateExePreview()
    {
        if (txtCmdPreviewExe is null)
        {
            return;
        }

        var path = txtExecutavel?.Text?.Trim() ?? string.Empty;
        var args = txtArgumentos?.Text?.Trim() ?? string.Empty;

        string preview;
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(args))
        {
            preview = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(path))
        {
            preview = args;
        }
        else if (string.IsNullOrWhiteSpace(args))
        {
            preview = $"\"{path}\"";
        }
        else
        {
            preview = $"\"{path}\" {args}";
        }

        txtCmdPreviewExe.Text = preview;
    }
}
