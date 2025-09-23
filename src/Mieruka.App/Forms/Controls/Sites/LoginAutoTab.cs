using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Automation.Login;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed class LoginAutoTab : UserControl
{
    private readonly LoginOrchestrator _orchestrator = new();
    private readonly TextBox _userSelectorBox;
    private readonly TextBox _passwordSelectorBox;
    private readonly TextBox _submitSelectorBox;
    private readonly TextBox _postSubmitSelectorBox;
    private readonly BindingList<string> _extraWaitSelectors = new();
    private readonly ListBox _extraWaitList;
    private readonly CheckBox _useHeuristicsBox;
    private readonly CheckBox _useJsSetValueBox;
    private readonly BindingList<string> _ssoHints = new();
    private readonly ListBox _ssoHintsList;
    private readonly ComboBox _mfaCombo;
    private readonly TextBox _totpRefBox;
    private readonly Button _detectButton;
    private readonly Button _testButton;
    private readonly Button _positionButton;
    private SiteConfig? _site;

    public event EventHandler<string>? TestLoginRequested;
    public event EventHandler<string>? ApplyPositionRequested;

    public LoginAutoTab()
    {
        LayoutHelpers.ApplyStandardLayout(this);
        AutoScroll = true;

        var layout = LayoutHelpers.CreateStandardTableLayout();
        layout.RowCount = 5;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var selectors = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        selectors.Controls.Add(new Label { Text = "UserSelector", AutoSize = true }, 0, 0);
        _userSelectorBox = new TextBox { Dock = DockStyle.Fill };
        selectors.Controls.Add(_userSelectorBox, 1, 0);

        selectors.Controls.Add(new Label { Text = "PasswordSelector", AutoSize = true }, 0, 1);
        _passwordSelectorBox = new TextBox { Dock = DockStyle.Fill };
        selectors.Controls.Add(_passwordSelectorBox, 1, 1);

        selectors.Controls.Add(new Label { Text = "SubmitSelector", AutoSize = true }, 0, 2);
        _submitSelectorBox = new TextBox { Dock = DockStyle.Fill };
        selectors.Controls.Add(_submitSelectorBox, 1, 2);

        selectors.Controls.Add(new Label { Text = "PostSubmitSelector", AutoSize = true }, 0, 3);
        _postSubmitSelectorBox = new TextBox { Dock = DockStyle.Fill };
        selectors.Controls.Add(_postSubmitSelectorBox, 1, 3);

        layout.Controls.Add(selectors, 0, 0);

        var toggles = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        _useHeuristicsBox = new CheckBox { Text = "UseHeuristicsFallback", AutoSize = true };
        _useJsSetValueBox = new CheckBox { Text = "UseJsSetValue", AutoSize = true };
        toggles.Controls.Add(_useHeuristicsBox);
        toggles.Controls.Add(_useJsSetValueBox);
        layout.Controls.Add(toggles, 0, 1);

        var extraSelectorsPanel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        extraSelectorsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        extraSelectorsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

        _extraWaitList = new ListBox { Dock = DockStyle.Fill, DataSource = _extraWaitSelectors };
        extraSelectorsPanel.Controls.Add(_extraWaitList, 0, 0);

        var extraButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var extraInput = new TextBox { Width = 180 };
        extraButtons.Controls.Add(extraInput);
        var addExtra = new Button { Text = "Adicionar", AutoSize = true };
        addExtra.Click += (_, _) => AddToList(extraInput, _extraWaitSelectors);
        extraButtons.Controls.Add(addExtra);
        var removeExtra = new Button { Text = "Remover", AutoSize = true };
        removeExtra.Click += (_, _) => RemoveSelected(_extraWaitList, _extraWaitSelectors);
        extraButtons.Controls.Add(removeExtra);
        extraSelectorsPanel.Controls.Add(extraButtons, 1, 0);

        layout.Controls.Add(extraSelectorsPanel, 0, 2);

        var ssoPanel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        ssoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        ssoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

        _ssoHintsList = new ListBox { Dock = DockStyle.Fill, DataSource = _ssoHints };
        ssoPanel.Controls.Add(_ssoHintsList, 0, 0);

        var ssoButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var ssoInput = new TextBox { Width = 180 };
        ssoButtons.Controls.Add(ssoInput);
        var addSso = new Button { Text = "Adicionar", AutoSize = true };
        addSso.Click += (_, _) => AddToList(ssoInput, _ssoHints);
        ssoButtons.Controls.Add(addSso);
        var removeSso = new Button { Text = "Remover", AutoSize = true };
        removeSso.Click += (_, _) => RemoveSelected(_ssoHintsList, _ssoHints);
        ssoButtons.Controls.Add(removeSso);
        ssoPanel.Controls.Add(ssoButtons, 1, 0);

        layout.Controls.Add(ssoPanel, 0, 3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _mfaCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 140,
        };
        _mfaCombo.Items.AddRange(new object[] { "TOTP", "Manual" });
        _mfaCombo.SelectedIndex = 0;
        footer.Controls.Add(new Label { Text = "MFA", AutoSize = true });
        footer.Controls.Add(_mfaCombo);

        _totpRefBox = new TextBox
        {
            ReadOnly = true,
            Width = 260,
        };
        footer.Controls.Add(new Label { Text = "TotpSecretKeyRef", AutoSize = true });
        footer.Controls.Add(_totpRefBox);

        _detectButton = new Button { Text = "Detectar Campos", AutoSize = true };
        _detectButton.Click += async (_, _) => await DetectarCamposAsync().ConfigureAwait(false);
        footer.Controls.Add(_detectButton);

        _testButton = new Button { Text = "Testar Login", AutoSize = true };
        _testButton.Click += (_, _) => RunLoginTest();
        footer.Controls.Add(_testButton);

        _positionButton = new Button { Text = "Aplicar Posição", AutoSize = true };
        _positionButton.Click += (_, _) => ApplyPosition();
        footer.Controls.Add(_positionButton);

        layout.Controls.Add(footer, 0, 4);

        Controls.Add(layout);
    }

    public void BindSite(SiteConfig? site)
    {
        _site = site;
        _totpRefBox.Text = site is null
            ? string.Empty
            : Mieruka.Core.Security.CredentialVault.BuildTotpKey(site.Id);
        Enabled = site is not null;
    }

    private async Task DetectarCamposAsync()
    {
        if (_site is null)
        {
            MessageBox.Show(this, "Selecione um site para detectar campos.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _detectButton.Enabled = false;
            await Task.Delay(250).ConfigureAwait(true);
            MessageBox.Show(this, "Detecção automática não está disponível nesta versão.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Falha ao detectar campos: {ex.Message}", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _detectButton.Enabled = true;
        }
    }

    private void AddToList(TextBox input, BindingList<string> target)
    {
        var text = input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        target.Add(text);
        input.Clear();
    }

    private void RemoveSelected(ListBox list, BindingList<string> target)
    {
        if (list.SelectedItem is string value)
        {
            target.Remove(value);
        }
    }

    private void RunLoginTest()
    {
        if (_site is null)
        {
            MessageBox.Show(this, "Selecione um site antes de testar.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var success = _orchestrator.EnsureLoggedIn(_site);
            var message = success ? "Login bem-sucedido." : "Falha ao efetuar login.";
            var icon = success ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            MessageBox.Show(this, message, "Login Automático", MessageBoxButtons.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao testar login: {ex.Message}", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        TestLoginRequested?.Invoke(this, _site.Id);
    }

    private void ApplyPosition()
    {
        if (_site is null)
        {
            MessageBox.Show(this, "Selecione um site antes de aplicar a posição.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            WindowMover.Apply(_site.Window);
            ApplyPositionRequested?.Invoke(this, _site.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao aplicar posição: {ex.Message}", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
