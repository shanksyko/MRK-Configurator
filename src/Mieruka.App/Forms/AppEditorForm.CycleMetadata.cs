#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
    private void InitializeCycleMetadata(IList<ProgramaConfig>? profileApps, ProgramaConfig? programa)
    {
        if (bsCycle is null || dgvCycle is null)
        {
            return;
        }

        var items = new List<ProfileItemMetadata>();

        if (profileApps is not null)
        {
            foreach (var app in profileApps)
            {
                var isTarget = programa is not null && ReferenceEquals(app, programa);
                var metadata = new ProfileItemMetadata(app, isTarget, items.Count + 1);
                items.Add(metadata);
                if (isTarget)
                {
                    _editingMetadata = metadata;
                }
            }
        }

        if (_editingMetadata is null)
        {
            var defaultOrder = items.Count > 0 ? items.Max(item => item.Order) + 1 : 1;
            var metadata = new ProfileItemMetadata(programa, isTarget: true, defaultOrder);
            items.Add(metadata);
            _editingMetadata = metadata;
        }

        _suppressCycleUpdates = true;
        try
        {
            foreach (var item in _profileItems)
            {
                item.PropertyChanged -= ProfileItem_PropertyChanged;
            }

            _profileItems.RaiseListChangedEvents = false;
            _profileItems.Clear();
            foreach (var item in items
                         .OrderBy(i => i.Order)
                         .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
            {
                item.PropertyChanged += ProfileItem_PropertyChanged;
                _profileItems.Add(item);
            }
            _profileItems.RaiseListChangedEvents = true;
            _profileItems.ResetBindings();

            RenumberCycleOrders();
        }
        finally
        {
            _suppressCycleUpdates = false;
        }

        bsCycle.DataSource = _profileItems;
        RefreshCyclePreviewNumbers();
        SelectCycleItem(_editingMetadata);
    }

    private void ProfileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressCycleUpdates)
        {
            return;
        }

        RefreshCyclePreviewNumbers();
    }

    private void RenumberCycleOrders()
    {
        _suppressCycleUpdates = true;
        try
        {
            for (var index = 0; index < _profileItems.Count; index++)
            {
                var item = _profileItems[index];
                item.Order = index + 1;
            }
        }
        finally
        {
            _suppressCycleUpdates = false;
        }

        RefreshCyclePreviewNumbers();
    }

    private void RefreshCyclePreviewNumbers()
    {
        if (_suppressCycleUpdates)
        {
            return;
        }

        UpdateCycleButtons();
        monitorPreviewDisplay?.Invalidate();
    }

    private void UpdateCycleButtons()
    {
        if (btnCycleUp is null || btnCycleDown is null || dgvCycle is null)
        {
            return;
        }

        if (dgvCycle.Rows.Count == 0)
        {
            btnCycleUp.Enabled = false;
            btnCycleDown.Enabled = false;
            return;
        }

        var index = dgvCycle.CurrentCell?.RowIndex ?? (dgvCycle.SelectedRows.Count > 0 ? dgvCycle.SelectedRows[0].Index : -1);
        btnCycleUp.Enabled = index > 0;
        btnCycleDown.Enabled = index >= 0 && index < dgvCycle.Rows.Count - 1;
    }

    private void SetCycleCurrentCellSafe(DataGridViewCell? cell)
    {
        if (dgvCycle is null)
        {
            return;
        }

        if (cell is null || cell.DataGridView != dgvCycle)
        {
            return;
        }

        if (_settingCycleCurrentCell)
        {
            return;
        }

        try
        {
            _settingCycleCurrentCell = true;
            dgvCycle.CurrentCell = cell;
        }
        finally
        {
            _settingCycleCurrentCell = false;
        }
    }

    private void SelectCycleItem(ProfileItemMetadata? item)
    {
        if (dgvCycle is null)
        {
            return;
        }

        if (item is null)
        {
            UpdateCycleButtons();
            return;
        }

        var index = _profileItems.IndexOf(item);
        if (index < 0 || index >= dgvCycle.Rows.Count)
        {
            UpdateCycleButtons();
            return;
        }

        _suppressCycleSelectionEvents = true;
        try
        {
            dgvCycle.ClearSelection();
            SetCycleCurrentCellSafe(dgvCycle.Rows[index].Cells[0]);
            dgvCycle.Rows[index].Selected = true;
        }
        finally
        {
            _suppressCycleSelectionEvents = false;
        }

        UpdateCycleButtons();
    }

    private void txtId_TextChanged(object? sender, EventArgs e)
    {
        if (_editingMetadata is null)
        {
            return;
        }

        _editingMetadata.Id = txtId.Text?.Trim() ?? string.Empty;
    }

    private void dgvCycle_SelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressCycleSelectionEvents)
        {
            return;
        }

        UpdateCycleButtons();
    }

    private void dgvCycle_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (dgvCycle?.IsCurrentCellDirty == true)
        {
            dgvCycle.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void dgvCycle_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        if (dgvCycle is null)
        {
            return;
        }

        var columnName = dgvCycle.Columns[e.ColumnIndex].Name;
        var metadata = e.RowIndex < _profileItems.Count ? _profileItems[e.RowIndex] : null;

        if (string.Equals(columnName, "colCycleOrder", StringComparison.Ordinal))
        {
            SortCycleItemsByOrder();
            RenumberCycleOrders();
            SelectCycleItem(metadata);
        }

        RefreshCyclePreviewNumbers();
    }

    private void dgvCycle_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    private void SortCycleItemsByOrder()
    {
        _suppressCycleUpdates = true;
        try
        {
            var ordered = _profileItems
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _profileItems.RaiseListChangedEvents = false;
            _profileItems.Clear();
            foreach (var item in ordered)
            {
                _profileItems.Add(item);
            }
            _profileItems.RaiseListChangedEvents = true;
            _profileItems.ResetBindings();
        }
        finally
        {
            _suppressCycleUpdates = false;
        }

        RefreshCyclePreviewNumbers();
    }

    private void btnCycleUp_Click(object? sender, EventArgs e)
    {
        if (dgvCycle is null)
        {
            return;
        }

        var index = dgvCycle.CurrentCell?.RowIndex ?? (dgvCycle.SelectedRows.Count > 0 ? dgvCycle.SelectedRows[0].Index : -1);
        if (index <= 0)
        {
            return;
        }

        var item = _profileItems[index];

        _profileItems.RaiseListChangedEvents = false;
        _profileItems.RemoveAt(index);
        _profileItems.Insert(index - 1, item);
        _profileItems.RaiseListChangedEvents = true;
        _profileItems.ResetBindings();

        RenumberCycleOrders();
        SelectCycleItem(item);
    }

    private void btnCycleDown_Click(object? sender, EventArgs e)
    {
        if (dgvCycle is null)
        {
            return;
        }

        var index = dgvCycle.CurrentCell?.RowIndex ?? (dgvCycle.SelectedRows.Count > 0 ? dgvCycle.SelectedRows[0].Index : -1);
        if (index < 0 || index >= _profileItems.Count - 1)
        {
            return;
        }

        var item = _profileItems[index];

        _profileItems.RaiseListChangedEvents = false;
        _profileItems.RemoveAt(index);
        _profileItems.Insert(index + 1, item);
        _profileItems.RaiseListChangedEvents = true;
        _profileItems.ResetBindings();

        RenumberCycleOrders();
        SelectCycleItem(item);
    }

    private void CommitProfileMetadata()
    {
        if (_profileApps is null)
        {
            return;
        }

        foreach (var metadata in _profileItems)
        {
            if (metadata.Original is null)
            {
                continue;
            }

            if (_original is not null && ReferenceEquals(metadata.Original, _original))
            {
                continue;
            }

            var index = FindProfileIndex(metadata.Original);
            if (index < 0)
            {
                continue;
            }

            var current = _profileApps[index];
            _profileApps[index] = current with
            {
                Order = metadata.Order,
                DelayMs = metadata.DelayMs,
                AskBeforeLaunch = metadata.AskBeforeLaunch,
                RequiresNetwork = metadata.RequiresNetwork ?? current.RequiresNetwork,
            };
        }
    }

    private int FindProfileIndex(ProgramaConfig target)
    {
        for (var i = 0; i < _profileApps!.Count; i++)
        {
            if (ReferenceEquals(_profileApps[i], target))
            {
                return i;
            }
        }

        return -1;
    }
}
