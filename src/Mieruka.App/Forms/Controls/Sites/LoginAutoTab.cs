using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Automation.Login;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

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
    private SiteConfig? _site;

    public event EventHandler<string>? TestLoginRequested;
    public event EventHandler<string>? ApplyPositionRequested;

    public LoginAutoTab(SecretsProvider secretsProvider)
    {
        _ = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
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

        var detectButton = new Button { Text = "Detectar Campos", AutoSize = true };
        detectButton.Click += (_, _) => MessageBox.Show(this, "Detecção automática não implementada.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Information);
        footer.Controls.Add(detectButton);

        var testButton = new Button { Text = "Testar Login", AutoSize = true };
        testButton.Click += (_, _) => RunLoginTest();
        footer.Controls.Add(testButton);

        var positionButton = new Button { Text = "Aplicar Posição", AutoSize = true };
        positionButton.Click += (_, _) => ApplyPosition();
        footer.Controls.Add(positionButton);

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
            return;
        }

        var success = _orchestrator.EnsureLoggedIn(_site);
        var message = success ? "Login bem-sucedido." : "Falha ao efetuar login.";
        MessageBox.Show(this, message, "Login Automático", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        TestLoginRequested?.Invoke(this, _site.Id);
    }

    private void ApplyPosition()
    {
        if (_site is null)
        {
            return;
        }

        WindowMover.Apply(_site.Window);
        ApplyPositionRequested?.Invoke(this, _site.Id);
    }
}
