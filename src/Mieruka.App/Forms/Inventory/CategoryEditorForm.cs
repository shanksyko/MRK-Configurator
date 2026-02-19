#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;

namespace Mieruka.App.Forms.Inventory;

/// <summary>
/// Editor de categorias: permite criar, editar, excluir, reordenar categorias
/// e definir campos customizados por categoria.
/// </summary>
public sealed class CategoryEditorForm : Form
{
    private readonly InventoryCategoryService _categoryService;
    private List<InventoryCategoryEntity> _categories = new();

    // Controls
    private readonly ListBox _lstCategories = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtDescription = new();
    private readonly TextBox _txtIcon = new();
    private readonly TextBox _txtColor = new();
    private readonly Panel _pnlColorPreview = new();
    private readonly DataGridView _gridCustomFields = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnDelete = new();
    private readonly Button _btnMoveUp = new();
    private readonly Button _btnMoveDown = new();
    private readonly Button _btnClose = new();

    private InventoryCategoryEntity? _selectedCategory;

    public CategoryEditorForm(InventoryCategoryService categoryService)
    {
        _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
        BuildLayout();
        Shown += async (_, _) => await LoadCategoriesAsync();
    }

    private void BuildLayout()
    {
        Text = "Gerenciar Categorias";
        MinimumSize = new Size(720, 520);
        ClientSize = new Size(760, 540);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        // ── Left panel: list + action buttons ──
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 220, Padding = new Padding(8) };

        var lblList = new Label
        {
            Text = "Categorias:",
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        _lstCategories.Dock = DockStyle.Fill;
        _lstCategories.SelectedIndexChanged += OnCategorySelected;

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 0),
        };

        _btnMoveUp.Text = "▲";
        _btnMoveUp.AutoSize = true;
        _btnMoveUp.Click += async (_, _) => await OnMoveUpAsync();
        _btnMoveDown.Text = "▼";
        _btnMoveDown.AutoSize = true;
        _btnMoveDown.Click += async (_, _) => await OnMoveDownAsync();
        _btnAdd.Text = "+ Nova";
        _btnAdd.AutoSize = true;
        _btnAdd.Click += OnAddClicked;
        _btnDelete.Text = "Excluir";
        _btnDelete.AutoSize = true;
        _btnDelete.ForeColor = Color.DarkRed;
        _btnDelete.Click += async (_, _) => await OnDeleteAsync();

        leftButtons.Controls.AddRange(new Control[] { _btnMoveUp, _btnMoveDown, _btnAdd, _btnDelete });

        leftPanel.Controls.Add(_lstCategories);
        leftPanel.Controls.Add(leftButtons);
        leftPanel.Controls.Add(lblList);

        // ── Right panel: details ──
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var detailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
        };
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72f));

        _txtName.Dock = DockStyle.Fill;
        _txtName.MaxLength = 120;
        AddRow(detailLayout, "Nome:*", _txtName);

        _txtDescription.Dock = DockStyle.Fill;
        AddRow(detailLayout, "Descrição:", _txtDescription);

        _txtIcon.Dock = DockStyle.Left;
        _txtIcon.Width = 120;
        AddRow(detailLayout, "Ícone (emoji):", _txtIcon);

        // Color row — text + preview side-by-side
        var colorPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        _txtColor.Width = 100;
        _txtColor.TextChanged += OnColorTextChanged;
        _pnlColorPreview.Size = new Size(24, 23);
        _pnlColorPreview.BorderStyle = BorderStyle.FixedSingle;
        _pnlColorPreview.Margin = new Padding(6, 0, 0, 0);
        colorPanel.Controls.Add(_txtColor);
        colorPanel.Controls.Add(_pnlColorPreview);
        AddRow(detailLayout, "Cor (hex):", colorPanel);

        // Custom fields label
        var lblFields = new Label
        {
            Text = "Campos Customizados:",
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var rowFields = detailLayout.RowCount++;
        detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailLayout.Controls.Add(lblFields, 0, rowFields);
        detailLayout.SetColumnSpan(lblFields, 2);

        // Custom fields grid — fill remaining space
        _gridCustomFields.Dock = DockStyle.Fill;
        _gridCustomFields.AllowUserToDeleteRows = true;
        _gridCustomFields.AutoGenerateColumns = false;
        _gridCustomFields.RowHeadersVisible = true;
        _gridCustomFields.BackgroundColor = SystemColors.Window;
        _gridCustomFields.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridCustomFields.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFieldName",
            HeaderText = "Nome do Campo",
            FillWeight = 40,
            MinimumWidth = 100,
        });
        var colType = new DataGridViewComboBoxColumn
        {
            Name = "colFieldType",
            HeaderText = "Tipo",
            FillWeight = 25,
            MinimumWidth = 80,
        };
        colType.Items.AddRange("text", "number", "date", "boolean", "select");
        _gridCustomFields.Columns.Add(colType);
        _gridCustomFields.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFieldOptions",
            HeaderText = "Opções (vírgula)",
            FillWeight = 35,
            MinimumWidth = 100,
        });

        var rowGrid = detailLayout.RowCount++;
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        detailLayout.Controls.Add(_gridCustomFields, 0, rowGrid);
        detailLayout.SetColumnSpan(_gridCustomFields, 2);

        rightPanel.Controls.Add(detailLayout);

        // ── Bottom button bar ──
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };

        _btnClose.Text = "Fechar";
        _btnClose.AutoSize = true;
        _btnClose.Click += (_, _) => Close();
        _btnSave.BackColor = Color.FromArgb(0, 120, 215);
        _btnSave.FlatStyle = FlatStyle.Flat;
        _btnSave.ForeColor = Color.White;
        _btnSave.Text = "Salvar Categoria";
        _btnSave.AutoSize = true;
        _btnSave.UseVisualStyleBackColor = false;
        _btnSave.Click += async (_, _) => await OnSaveAsync();

        buttonBar.Controls.Add(_btnClose);
        buttonBar.Controls.Add(_btnSave);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(buttonBar);

        CancelButton = _btnClose;
        UpdateButtonStates();
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 6),
        }, 0, row);
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    private async Task LoadCategoriesAsync()
    {
        try
        {
            _categories = (await _categoryService.GetAllAsync().ConfigureAwait(true)).ToList();
            var selectedIndex = _lstCategories.SelectedIndex;
            _lstCategories.BeginUpdate();
            _lstCategories.Items.Clear();
            foreach (var cat in _categories)
            {
                var icon = string.IsNullOrWhiteSpace(cat.Icon) ? "" : cat.Icon + " ";
                _lstCategories.Items.Add($"{icon}{cat.Name}");
            }
            _lstCategories.EndUpdate();

            if (_categories.Count > 0)
            {
                _lstCategories.SelectedIndex = Math.Min(Math.Max(0, selectedIndex), _categories.Count - 1);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar categorias: {ex.Message}", "Categorias",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnCategorySelected(object? sender, EventArgs e)
    {
        var index = _lstCategories.SelectedIndex;
        if (index < 0 || index >= _categories.Count)
        {
            _selectedCategory = null;
            ClearFields();
            UpdateButtonStates();
            return;
        }

        _selectedCategory = _categories[index];
        FillFields(_selectedCategory);
        UpdateButtonStates();
    }

    private void FillFields(InventoryCategoryEntity cat)
    {
        _txtName.Text = cat.Name;
        _txtDescription.Text = cat.Description ?? string.Empty;
        _txtIcon.Text = cat.Icon ?? string.Empty;
        _txtColor.Text = cat.Color ?? string.Empty;

        // Custom fields
        _gridCustomFields.Rows.Clear();
        if (!string.IsNullOrWhiteSpace(cat.CustomFieldsJson))
        {
            try
            {
                var fields = JsonSerializer.Deserialize<List<CustomFieldDefinition>>(cat.CustomFieldsJson);
                if (fields is not null)
                {
                    foreach (var f in fields)
                    {
                        var rowIdx = _gridCustomFields.Rows.Add();
                        var row = _gridCustomFields.Rows[rowIdx];
                        row.Cells["colFieldName"].Value = f.Name;
                        row.Cells["colFieldType"].Value = f.Type;
                        row.Cells["colFieldOptions"].Value = f.Options;
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON
            }
        }
    }

    private void ClearFields()
    {
        _txtName.Text = string.Empty;
        _txtDescription.Text = string.Empty;
        _txtIcon.Text = string.Empty;
        _txtColor.Text = string.Empty;
        _gridCustomFields.Rows.Clear();
    }

    private void UpdateButtonStates()
    {
        var hasSel = _selectedCategory is not null;
        _btnSave.Enabled = true;
        _btnDelete.Enabled = hasSel;
        _btnMoveUp.Enabled = hasSel && _lstCategories.SelectedIndex > 0;
        _btnMoveDown.Enabled = hasSel && _lstCategories.SelectedIndex < _categories.Count - 1;
    }

    private void OnColorTextChanged(object? sender, EventArgs e)
    {
        try
        {
            var hex = _txtColor.Text.Trim();
            if (!string.IsNullOrEmpty(hex))
            {
                _pnlColorPreview.BackColor = ColorTranslator.FromHtml(hex);
            }
            else
            {
                _pnlColorPreview.BackColor = SystemColors.Control;
            }
        }
        catch
        {
            _pnlColorPreview.BackColor = SystemColors.Control;
        }
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        _selectedCategory = null;
        _lstCategories.ClearSelected();
        ClearFields();
        _txtName.Focus();
    }

    private async Task OnSaveAsync()
    {
        var name = _txtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("O nome da categoria é obrigatório.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtName.Focus();
            return;
        }

        try
        {
            var entity = _selectedCategory ?? new InventoryCategoryEntity();
            entity.Name = name;
            entity.Description = NullIfEmpty(_txtDescription.Text);
            entity.Icon = NullIfEmpty(_txtIcon.Text);
            entity.Color = NullIfEmpty(_txtColor.Text);
            entity.CustomFieldsJson = SerializeCustomFields();

            if (_selectedCategory is null)
            {
                await _categoryService.CreateAsync(entity).ConfigureAwait(true);
            }
            else
            {
                await _categoryService.UpdateAsync(entity).ConfigureAwait(true);
            }

            await LoadCategoriesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao salvar categoria: {ex.Message}", "Categorias",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnDeleteAsync()
    {
        if (_selectedCategory is null) return;

        var result = MessageBox.Show(
            $"Excluir a categoria '{_selectedCategory.Name}'?\n\nItens associados perderão o vínculo de categoria.",
            "Excluir Categoria",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        try
        {
            await _categoryService.DeleteAsync(_selectedCategory.Id).ConfigureAwait(true);
            _selectedCategory = null;
            ClearFields();
            await LoadCategoriesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao excluir categoria: {ex.Message}", "Categorias",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnMoveUpAsync()
    {
        if (_selectedCategory is null) return;
        await _categoryService.MoveUpAsync(_selectedCategory.Id).ConfigureAwait(true);
        var idx = _lstCategories.SelectedIndex;
        await LoadCategoriesAsync().ConfigureAwait(true);
        if (idx > 0)
            _lstCategories.SelectedIndex = idx - 1;
    }

    private async Task OnMoveDownAsync()
    {
        if (_selectedCategory is null) return;
        await _categoryService.MoveDownAsync(_selectedCategory.Id).ConfigureAwait(true);
        var idx = _lstCategories.SelectedIndex;
        await LoadCategoriesAsync().ConfigureAwait(true);
        if (idx < _categories.Count - 1)
            _lstCategories.SelectedIndex = idx + 1;
    }

    private string? SerializeCustomFields()
    {
        var fields = new List<CustomFieldDefinition>();
        foreach (DataGridViewRow row in _gridCustomFields.Rows)
        {
            if (row.IsNewRow) continue;
            var name = row.Cells["colFieldName"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            fields.Add(new CustomFieldDefinition
            {
                Name = name.Trim(),
                Type = row.Cells["colFieldType"].Value?.ToString() ?? "text",
                Options = row.Cells["colFieldOptions"].Value?.ToString(),
            });
        }
        return fields.Count > 0
            ? JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = false })
            : null;
    }

    private static string? NullIfEmpty(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed class CustomFieldDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "text";
        public string? Options { get; set; }
    }
}
