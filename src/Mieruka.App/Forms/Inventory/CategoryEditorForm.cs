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
        ClientSize = new Size(720, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        // ── Left panel: list + reorder buttons ──
        var lblList = new Label { Text = "Categorias:", AutoSize = true, Location = new Point(12, 12) };
        Controls.Add(lblList);

        _lstCategories.Location = new Point(12, 32);
        _lstCategories.Size = new Size(200, 360);
        _lstCategories.SelectedIndexChanged += OnCategorySelected;
        Controls.Add(_lstCategories);

        _btnMoveUp.Text = "▲";
        _btnMoveUp.Size = new Size(40, 28);
        _btnMoveUp.Location = new Point(12, 398);
        _btnMoveUp.Click += async (_, _) => await OnMoveUpAsync();
        Controls.Add(_btnMoveUp);

        _btnMoveDown.Text = "▼";
        _btnMoveDown.Size = new Size(40, 28);
        _btnMoveDown.Location = new Point(56, 398);
        _btnMoveDown.Click += async (_, _) => await OnMoveDownAsync();
        Controls.Add(_btnMoveDown);

        _btnAdd.Text = "+ Nova";
        _btnAdd.Size = new Size(80, 28);
        _btnAdd.Location = new Point(100, 398);
        _btnAdd.Click += OnAddClicked;
        Controls.Add(_btnAdd);

        _btnDelete.Text = "Excluir";
        _btnDelete.Size = new Size(72, 28);
        _btnDelete.Location = new Point(140, 432);
        _btnDelete.ForeColor = Color.DarkRed;
        _btnDelete.Click += async (_, _) => await OnDeleteAsync();
        Controls.Add(_btnDelete);

        // ── Right panel: details ──
        int x = 230, y = 12;

        AddField("Nome:*", _txtName, x, ref y, 240);
        _txtName.MaxLength = 120;

        AddField("Descrição:", _txtDescription, x, ref y, 240);
        AddField("Ícone (emoji):", _txtIcon, x, ref y, 120);

        // Color
        var lblColor = new Label { Text = "Cor (hex):", AutoSize = true, Location = new Point(x, y + 3) };
        Controls.Add(lblColor);
        _txtColor.Location = new Point(x + 120, y);
        _txtColor.Size = new Size(100, 23);
        _txtColor.TextChanged += OnColorTextChanged;
        Controls.Add(_txtColor);

        _pnlColorPreview.Location = new Point(x + 226, y);
        _pnlColorPreview.Size = new Size(24, 23);
        _pnlColorPreview.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_pnlColorPreview);
        y += 32;

        // Custom fields grid
        var lblFields = new Label { Text = "Campos Customizados:", AutoSize = true, Location = new Point(x, y) };
        Controls.Add(lblFields);
        y += 20;

        _gridCustomFields.Location = new Point(x, y);
        _gridCustomFields.Size = new Size(470, 200);
        _gridCustomFields.AllowUserToDeleteRows = true;
        _gridCustomFields.AutoGenerateColumns = false;
        _gridCustomFields.RowHeadersVisible = true;
        _gridCustomFields.BackgroundColor = SystemColors.Window;
        _gridCustomFields.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFieldName",
            HeaderText = "Nome do Campo",
            Width = 200,
        });
        var colType = new DataGridViewComboBoxColumn
        {
            Name = "colFieldType",
            HeaderText = "Tipo",
            Width = 120,
        };
        colType.Items.AddRange("text", "number", "date", "boolean", "select");
        _gridCustomFields.Columns.Add(colType);
        _gridCustomFields.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFieldOptions",
            HeaderText = "Opções (vírgula)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        Controls.Add(_gridCustomFields);
        y += 210;

        // Save + Close buttons
        _btnSave.BackColor = Color.FromArgb(0, 120, 215);
        _btnSave.FlatStyle = FlatStyle.Flat;
        _btnSave.ForeColor = Color.White;
        _btnSave.Text = "Salvar Categoria";
        _btnSave.Size = new Size(150, 32);
        _btnSave.Location = new Point(x, y);
        _btnSave.UseVisualStyleBackColor = false;
        _btnSave.Click += async (_, _) => await OnSaveAsync();
        Controls.Add(_btnSave);

        _btnClose.Text = "Fechar";
        _btnClose.Size = new Size(100, 32);
        _btnClose.Location = new Point(x + 164, y);
        _btnClose.Click += (_, _) => Close();
        Controls.Add(_btnClose);

        CancelButton = _btnClose;
        UpdateButtonStates();
    }

    private void AddField(string labelText, TextBox txt, int x, ref int y, int width)
    {
        var lbl = new Label { AutoSize = true, Location = new Point(x, y + 3), Text = labelText };
        Controls.Add(lbl);
        txt.Location = new Point(x + 120, y);
        txt.Size = new Size(width, 23);
        Controls.Add(txt);
        y += 32;
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
