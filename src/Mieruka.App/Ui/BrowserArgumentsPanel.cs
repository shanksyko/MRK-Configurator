#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Ui;

/// <summary>
/// A control that displays browser startup arguments as checkable items,
/// grouped by category, with inline value entry for arguments that require one.
/// </summary>
internal sealed class BrowserArgumentsPanel : WinForms.UserControl
{
    private readonly WinForms.FlowLayoutPanel _categoriesPanel;
    private readonly WinForms.TextBox _customArgsBox;
    private readonly Dictionary<string, ArgumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private BrowserType _currentBrowser = BrowserType.Chrome;

    /// <summary>
    /// Raised whenever the user changes argument selections or values.
    /// </summary>
    public event EventHandler? ArgumentsChanged;

    public BrowserArgumentsPanel()
    {
        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var outerLayout = new WinForms.TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            AutoSize = false,
        };
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
        outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _categoriesPanel = new WinForms.FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4),
        };

        var customLabel = new WinForms.Label
        {
            Text = "Argumentos adicionais (um por linha):",
            Font = baseFont,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 2),
        };

        _customArgsBox = new WinForms.TextBox
        {
            Dock = DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
        };
        _customArgsBox.TextChanged += (_, _) => OnArgumentsChanged();

        outerLayout.Controls.Add(_categoriesPanel, 0, 0);
        outerLayout.Controls.Add(customLabel, 0, 1);
        outerLayout.Controls.Add(_customArgsBox, 0, 2);

        Controls.Add(outerLayout);
    }

    /// <summary>
    /// Rebuilds the checklist for the given browser type.
    /// </summary>
    public void SetBrowser(BrowserType browser)
    {
        if (_currentBrowser == browser && _entries.Count > 0)
        {
            return;
        }

        _currentBrowser = browser;
        RebuildChecklist();
    }

    /// <summary>
    /// Returns all selected arguments as a flat list of strings,
    /// including values for arguments that require them and any custom arguments.
    /// </summary>
    public List<string> GetSelectedArguments()
    {
        var result = new List<string>();

        foreach (var entry in _entries.Values)
        {
            if (!entry.CheckBox.Checked)
            {
                continue;
            }

            if (entry.Definition.RequiresValue)
            {
                var value = entry.ValueBox?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(entry.Definition.BuildArgument(value));
                }
                // Skip arguments that require a value but have none.
            }
            else
            {
                result.Add(entry.Definition.Flag);
            }
        }

        // Append custom arguments.
        if (!string.IsNullOrWhiteSpace(_customArgsBox.Text))
        {
            foreach (var line in _customArgsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Loads existing arguments into the control, checking matching items and
    /// placing unrecognized arguments into the custom args box.
    /// </summary>
    public void LoadArguments(IReadOnlyList<string>? arguments)
    {
        // Uncheck all first.
        foreach (var entry in _entries.Values)
        {
            entry.CheckBox.Checked = false;
            if (entry.ValueBox is not null)
            {
                entry.ValueBox.Text = string.Empty;
            }
        }

        _customArgsBox.Text = string.Empty;

        if (arguments is null || arguments.Count == 0)
        {
            return;
        }

        var customLines = new List<string>();

        foreach (var arg in arguments)
        {
            var matched = TryMatchArgument(arg);
            if (!matched)
            {
                customLines.Add(arg);
            }
        }

        if (customLines.Count > 0)
        {
            _customArgsBox.Text = string.Join(Environment.NewLine, customLines);
        }
    }

    private bool TryMatchArgument(string argumentText)
    {
        if (string.IsNullOrWhiteSpace(argumentText))
        {
            return false;
        }

        // Try exact match first.
        if (_entries.TryGetValue(argumentText, out var exactEntry))
        {
            exactEntry.CheckBox.Checked = true;
            return true;
        }

        // Try matching by flag prefix (for arguments with values like "--proxy-server=host:port").
        var eqIndex = argumentText.IndexOf('=');
        if (eqIndex > 0)
        {
            var flagPart = argumentText[..eqIndex];
            var valuePart = argumentText[(eqIndex + 1)..];

            if (_entries.TryGetValue(flagPart, out var prefixEntry))
            {
                prefixEntry.CheckBox.Checked = true;
                if (prefixEntry.ValueBox is not null)
                {
                    prefixEntry.ValueBox.Text = valuePart;
                }

                return true;
            }
        }

        return false;
    }

    private void RebuildChecklist()
    {
        _categoriesPanel.SuspendLayout();
        _categoriesPanel.Controls.Clear();
        _entries.Clear();

        var categories = BrowserArgumentsCatalog.CategoriesFor(_currentBrowser);

        foreach (var category in categories)
        {
            var args = BrowserArgumentsCatalog.ForBrowser(_currentBrowser, category);
            if (args.Count == 0)
            {
                continue;
            }

            AddCategoryGroup(category, args);
        }

        _categoriesPanel.ResumeLayout(true);
    }

    private void AddCategoryGroup(BrowserArgumentCategory category, IReadOnlyList<BrowserArgumentDefinition> arguments)
    {
        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        var boldFont = new Font(baseFont, FontStyle.Bold);

        var categoryLabel = new WinForms.Label
        {
            Text = GetCategoryDisplayName(category),
            Font = boldFont,
            AutoSize = true,
            Padding = new Padding(2, 6, 2, 2),
            ForeColor = Color.FromArgb(60, 60, 60),
        };

        _categoriesPanel.Controls.Add(categoryLabel);
        _categoriesPanel.SetFlowBreak(categoryLabel, true);

        foreach (var arg in arguments)
        {
            var entryPanel = new WinForms.FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(12, 1, 2, 1),
            };

            var check = new WinForms.CheckBox
            {
                Text = arg.DisplayName,
                Font = baseFont,
                AutoSize = true,
                Tag = arg,
            };

            var tooltip = new WinForms.ToolTip();
            tooltip.SetToolTip(check, $"{arg.Flag}\n{arg.Description}");

            check.CheckedChanged += (_, _) => OnArgumentsChanged();

            entryPanel.Controls.Add(check);

            WinForms.TextBox? valueBox = null;
            if (arg.RequiresValue)
            {
                valueBox = new WinForms.TextBox
                {
                    Width = 180,
                    Font = baseFont,
                    PlaceholderText = arg.ValueHint ?? "valor",
                    Enabled = false,
                    Margin = new Padding(4, 2, 2, 2),
                };

                check.CheckedChanged += (_, _) =>
                {
                    valueBox.Enabled = check.Checked;
                };

                valueBox.TextChanged += (_, _) => OnArgumentsChanged();
                entryPanel.Controls.Add(valueBox);
            }

            _categoriesPanel.Controls.Add(entryPanel);
            _categoriesPanel.SetFlowBreak(entryPanel, true);

            _entries[arg.Flag] = new ArgumentEntry(arg, check, valueBox);
        }
    }

    private void OnArgumentsChanged()
    {
        ArgumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string GetCategoryDisplayName(BrowserArgumentCategory category)
    {
        return category switch
        {
            BrowserArgumentCategory.Display => "🖥️ Exibição",
            BrowserArgumentCategory.Privacy => "🔒 Privacidade",
            BrowserArgumentCategory.Security => "🛡️ Segurança",
            BrowserArgumentCategory.Performance => "⚡ Desempenho",
            BrowserArgumentCategory.Network => "🌐 Rede",
            BrowserArgumentCategory.Content => "📄 Conteúdo",
            BrowserArgumentCategory.Debug => "🐛 Depuração",
            _ => category.ToString(),
        };
    }

    private sealed record class ArgumentEntry(
        BrowserArgumentDefinition Definition,
        WinForms.CheckBox CheckBox,
        WinForms.TextBox? ValueBox);
}
