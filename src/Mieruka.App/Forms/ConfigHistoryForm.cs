#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Data;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms;

/// <summary>
/// Displays configuration snapshots and allows rollback.
/// </summary>
internal sealed class ConfigHistoryForm : Form
{
    private readonly ListView _listView;
    private readonly Button _btnRestore;
    private readonly Button _btnDelete;
    private readonly Button _btnClose;
    private List<ConfigSnapshotEntity> _snapshots = new();

    /// <summary>
    /// When a snapshot is restored, this event provides the restored <see cref="GeneralConfig"/>.
    /// </summary>
    public event EventHandler<GeneralConfig>? ConfigRestored;

    public ConfigHistoryForm()
    {
        Text = "Histórico de Configuração";
        Size = new Size(650, 450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(500, 350);
        DoubleBuffered = true;

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
        };
        _listView.Columns.Add("Data", 180);
        _listView.Columns.Add("Label", -2);
        _listView.Columns.Add("Tamanho", 100);
        _listView.SelectedIndexChanged += (_, _) => UpdateButtons();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(8),
        };

        _btnRestore = new Button { Text = "Restaurar", AutoSize = true, Enabled = false };
        _btnRestore.Click += async (_, _) => await RestoreSelectedAsync();
        buttonPanel.Controls.Add(_btnRestore);

        _btnDelete = new Button { Text = "Excluir", AutoSize = true, Enabled = false };
        _btnDelete.Click += async (_, _) => await DeleteSelectedAsync();
        buttonPanel.Controls.Add(_btnDelete);

        _btnClose = new Button { Text = "Fechar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(_btnClose);

        Controls.Add(_listView);
        Controls.Add(buttonPanel);
        CancelButton = _btnClose;

        Load += async (_, _) => await LoadSnapshotsAsync();
    }

    private void UpdateButtons()
    {
        var hasSelection = _listView.SelectedItems.Count > 0;
        _btnRestore.Enabled = hasSelection;
        _btnDelete.Enabled = hasSelection;
    }

    private async Task LoadSnapshotsAsync()
    {
        try
        {
            using var db = new MierukaDbContext();
            var service = new ConfigSnapshotService(db);
            _snapshots = (await service.GetAllSnapshotsAsync()).ToList();
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao carregar snapshots: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshList()
    {
        _listView.Items.Clear();
        foreach (var snapshot in _snapshots)
        {
            var size = snapshot.ConfigJson?.Length ?? 0;
            var sizeText = size > 1024 ? $"{size / 1024} KB" : $"{size} B";
            var item = new ListViewItem(new[]
            {
                snapshot.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                snapshot.Label,
                sizeText,
            })
            {
                Tag = snapshot.Id,
            };
            _listView.Items.Add(item);
        }
        UpdateButtons();
    }

    private int GetSelectedSnapshotId()
    {
        if (_listView.SelectedItems.Count == 0) return -1;
        return _listView.SelectedItems[0].Tag is int id ? id : -1;
    }

    private async Task RestoreSelectedAsync()
    {
        var id = GetSelectedSnapshotId();
        if (id < 0) return;

        var result = MessageBox.Show(
            this,
            "Deseja restaurar esta configuração? A configuração atual será substituída.",
            "Restaurar Configuração",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        try
        {
            using var db = new MierukaDbContext();
            var service = new ConfigSnapshotService(db);
            var config = await service.RestoreSnapshotAsync(id);
            if (config is not null)
            {
                ConfigRestored?.Invoke(this, config);
                MessageBox.Show(this, "Configuração restaurada com sucesso.", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "Falha ao restaurar o snapshot.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        var id = GetSelectedSnapshotId();
        if (id < 0) return;

        var result = MessageBox.Show(
            this,
            "Deseja excluir este snapshot?",
            "Excluir Snapshot",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        try
        {
            using var db = new MierukaDbContext();
            var service = new ConfigSnapshotService(db);
            await service.DeleteSnapshotAsync(id);
            await LoadSnapshotsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
