#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Data;
using Mieruka.Core.Data.Services;

namespace Mieruka.App.Forms;

/// <summary>
/// Dialog for configuring and executing data retention policies.
/// </summary>
internal sealed class DataRetentionForm : Form
{
    private readonly NumericUpDown _nudAuditDays;
    private readonly NumericUpDown _nudMovementDays;
    private readonly NumericUpDown _nudMaintenanceDays;
    private readonly NumericUpDown _nudSessionDays;
    private readonly Label _lblPreview;
    private readonly Button _btnPreview;
    private readonly Button _btnPurge;
    private readonly Button _btnSave;
    private readonly Button _btnClose;

    public DataRetentionForm()
    {
        Text = "Retenção de Dados";
        Size = new System.Drawing.Size(460, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        _nudAuditDays = AddRow(layout, "Logs de Auditoria (dias):", 0, 90);
        _nudMovementDays = AddRow(layout, "Movimentações de Inventário (dias):", 1, 180);
        _nudMaintenanceDays = AddRow(layout, "Manutenções Concluídas (dias):", 2, 365);
        _nudSessionDays = AddRow(layout, "Sessões Expiradas (dias):", 3, 30);

        _lblPreview = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Text = "Clique em 'Visualizar' para ver quantos registros serão removidos.",
        };
        layout.SetColumnSpan(_lblPreview, 2);
        layout.Controls.Add(_lblPreview, 0, 4);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
        };
        layout.SetColumnSpan(buttonPanel, 2);
        layout.Controls.Add(buttonPanel, 0, 5);

        _btnPreview = new Button { Text = "Visualizar", AutoSize = true };
        _btnPreview.Click += async (_, _) => await PreviewAsync();
        buttonPanel.Controls.Add(_btnPreview);

        _btnPurge = new Button { Text = "Limpar Agora", AutoSize = true };
        _btnPurge.Click += async (_, _) => await PurgeAsync();
        buttonPanel.Controls.Add(_btnPurge);

        _btnSave = new Button { Text = "Salvar Configurações", AutoSize = true };
        _btnSave.Click += async (_, _) => await SaveAsync();
        buttonPanel.Controls.Add(_btnSave);

        _btnClose = new Button { Text = "Fechar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(_btnClose);

        Controls.Add(layout);
        CancelButton = _btnClose;

        Load += async (_, _) => await LoadSettingsAsync();
    }

    private static NumericUpDown AddRow(TableLayoutPanel layout, string label, int row, int defaultValue)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        }, 0, row);

        var nud = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 9999,
            Value = defaultValue,
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(nud, 1, row);
        return nud;
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            using var db = new MierukaDbContext();
            var service = new DataRetentionService(db);
            var settings = await service.GetRetentionSettingsAsync();
            _nudAuditDays.Value = settings.AuditLogDays;
            _nudMovementDays.Value = settings.MovementDays;
            _nudMaintenanceDays.Value = settings.MaintenanceDays;
            _nudSessionDays.Value = settings.SessionDays;
        }
        catch (Exception ex)
        {
            _lblPreview.Text = $"Erro ao carregar configurações: {ex.Message}";
        }
    }

    private RetentionSettings BuildSettings() => new()
    {
        AuditLogDays = (int)_nudAuditDays.Value,
        MovementDays = (int)_nudMovementDays.Value,
        MaintenanceDays = (int)_nudMaintenanceDays.Value,
        SessionDays = (int)_nudSessionDays.Value,
    };

    private async Task PreviewAsync()
    {
        try
        {
            _btnPreview.Enabled = false;
            using var db = new MierukaDbContext();
            var service = new DataRetentionService(db);
            var preview = await service.PreviewPurgeAsync(BuildSettings());

            _lblPreview.Text =
                $"Registros a remover: {preview.Total}\n" +
                $"  Audit: {preview.AuditLogCount} | Movimentações: {preview.MovementCount}\n" +
                $"  Manutenções: {preview.MaintenanceCount} | Sessões: {preview.SessionCount}";
        }
        catch (Exception ex)
        {
            _lblPreview.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            _btnPreview.Enabled = true;
        }
    }

    private async Task PurgeAsync()
    {
        var result = MessageBox.Show(
            this,
            "Deseja realmente limpar os dados antigos? Esta ação não pode ser desfeita.",
            "Confirmar Limpeza",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            _btnPurge.Enabled = false;
            var settings = BuildSettings();
            using var db = new MierukaDbContext();
            var service = new DataRetentionService(db);

            var total = 0;
            total += await service.PurgeAuditLogsAsync(settings.AuditLogDays);
            total += await service.PurgeMovementsAsync(settings.MovementDays);
            total += await service.PurgeMaintenanceRecordsAsync(settings.MaintenanceDays);
            total += await service.PurgeSessionsAsync(settings.SessionDays);

            _lblPreview.Text = $"{total} registros removidos com sucesso.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao limpar dados: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnPurge.Enabled = true;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            using var db = new MierukaDbContext();
            var service = new DataRetentionService(db);
            await service.SaveRetentionSettingsAsync(BuildSettings());
            _lblPreview.Text = "Configurações salvas com sucesso.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao salvar: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
