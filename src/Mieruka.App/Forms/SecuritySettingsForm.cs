using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Security;
using Mieruka.Core.Security.Policy;

namespace Mieruka.App.Forms;

/// <summary>
/// Provides a configuration surface for security sensitive settings.
/// </summary>
public sealed class SecuritySettingsForm : Form
{
    private readonly UrlAllowlist _allowlist;
    private readonly SecurityPolicy _policy;
    private readonly CookieSafeStore _cookieStore;
    private readonly IntegrityService _integrity;
    private readonly AuditLog _auditLog;
    private readonly IntegrityManifest? _manifest;

    private readonly ComboBox _policyCombo;
    private readonly ListBox _allowListBox;
    private readonly TextBox _hostInput;
    private readonly ListBox _cookieHosts;
    private readonly CheckBox _telemetryOptIn;
    private readonly Label _integrityStatus;
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecuritySettingsForm"/> class.
    /// </summary>
    public SecuritySettingsForm(
        UrlAllowlist allowlist,
        SecurityPolicy policy,
        CookieSafeStore cookieStore,
        IntegrityService integrityService,
        AuditLog auditLog,
        IntegrityManifest? manifest = null)
    {
        _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _cookieStore = cookieStore ?? throw new ArgumentNullException(nameof(cookieStore));
        _integrity = integrityService ?? throw new ArgumentNullException(nameof(integrityService));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _manifest = manifest;

        Text = "Segurança";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 480);
        MinimumSize = new Size(600, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(8),
            AutoSize = true,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // Policy selector
        var policyLabel = new Label
        {
            Text = "Perfil de política",
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        _policyCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DataSource = Enum.GetValues(typeof(SecurityProfile)),
        };
        _policyCombo.SelectedItem = _policy.Profile;
        _policyCombo.SelectedIndexChanged += (_, _) => ApplyPolicy();

        layout.Controls.Add(policyLabel, 0, 0);
        layout.Controls.Add(_policyCombo, 1, 0);

        // Allowlist controls
        _allowListBox = new ListBox { Dock = DockStyle.Fill }; 
        _hostInput = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Adicionar host" };
        var addHostButton = new Button { Text = "Adicionar", Dock = DockStyle.Fill };
        var removeHostButton = new Button { Text = "Remover", Dock = DockStyle.Fill };

        addHostButton.Click += (_, _) => AddAllowlistEntry();
        removeHostButton.Click += (_, _) => RemoveAllowlistEntry();

        var allowListPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
        };
        allowListPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        allowListPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        allowListPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        allowListPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        allowListPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        allowListPanel.Controls.Add(_hostInput, 0, 0);
        allowListPanel.Controls.Add(addHostButton, 1, 0);
        allowListPanel.SetColumnSpan(_allowListBox, 2);
        allowListPanel.Controls.Add(_allowListBox, 0, 1);
        allowListPanel.Controls.Add(removeHostButton, 1, 2);

        var allowListGroup = new GroupBox
        {
            Text = "Hosts permitidos",
            Dock = DockStyle.Fill,
        };
        allowListGroup.Controls.Add(allowListPanel);

        layout.Controls.Add(allowListGroup, 0, 1);
        layout.SetColumnSpan(allowListGroup, 2);

        // Cookie management
        _cookieHosts = new ListBox { Dock = DockStyle.Fill };
        var revokeButton = new Button { Text = "Revogar cookies", Dock = DockStyle.Fill };
        revokeButton.Click += (_, _) => RevokeSelectedCookie();

        var cookieGroup = new GroupBox
        {
            Text = "Cookies armazenados",
            Dock = DockStyle.Fill,
        };
        var cookiePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        cookiePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cookiePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cookiePanel.Controls.Add(_cookieHosts, 0, 0);
        cookiePanel.Controls.Add(revokeButton, 0, 1);
        cookieGroup.Controls.Add(cookiePanel);

        layout.Controls.Add(cookieGroup, 0, 2);
        layout.SetColumnSpan(cookieGroup, 2);

        // Telemetry options
        _telemetryOptIn = new CheckBox
        {
            Text = "Permitir telemetria anônima",
            Dock = DockStyle.Fill,
        };
        _telemetryOptIn.CheckedChanged += (_, _) => _auditLog.WriteEvent(new AuditLog.AuditEvent("telemetry")
        {
            Result = _telemetryOptIn.Checked ? "enabled" : "disabled",
        });

        layout.Controls.Add(_telemetryOptIn, 0, 3);
        layout.SetColumnSpan(_telemetryOptIn, 2);

        // Integrity status
        _integrityStatus = new Label
        {
            Text = "Integridade: não verificada",
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var revalidateButton = new Button
        {
            Text = "Revalidar",
            Dock = DockStyle.Right,
        };
        revalidateButton.Click += (_, _) => RevalidateIntegrity();

        layout.Controls.Add(_integrityStatus, 0, 4);
        layout.Controls.Add(revalidateButton, 1, 4);

        Controls.Add(layout);

        Load += (_, _) => RefreshData();

        _cleanupTimer = new Timer { Interval = (int)TimeSpan.FromMinutes(15).TotalMilliseconds };
        _cleanupTimer.Tick += (_, _) => _cookieStore.PurgeExpired();
        _cleanupTimer.Start();
    }

    /// <summary>
    /// Gets or sets a value indicating whether telemetry is enabled.
    /// </summary>
    public bool TelemetryEnabled
    {
        get => _telemetryOptIn.Checked;
        set => _telemetryOptIn.Checked = value;
    }

    private void RefreshData()
    {
        _allowListBox.Items.Clear();
        foreach (var entry in _allowlist.GetGlobalEntries().OrderBy(host => host, StringComparer.OrdinalIgnoreCase))
        {
            _allowListBox.Items.Add(entry);
        }

        RefreshCookieList();
        UpdateIntegrityStatus("não verificada");
    }

    private void RefreshCookieList()
    {
        _cookieHosts.Items.Clear();
        foreach (var host in _cookieStore.EnumerateHosts().OrderBy(host => host, StringComparer.OrdinalIgnoreCase))
        {
            _cookieHosts.Items.Add(host);
        }
    }

    private void AddAllowlistEntry()
    {
        try
        {
            var host = InputSanitizer.SanitizeHost(_hostInput.Text);
            if (string.IsNullOrEmpty(host))
            {
                return;
            }

            _allowlist.Add(host);
            _hostInput.Clear();
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Host inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveAllowlistEntry()
    {
        if (_allowListBox.SelectedItem is string host)
        {
            _allowlist.Remove(host);
            RefreshData();
        }
    }

    private void RevokeSelectedCookie()
    {
        if (_cookieHosts.SelectedItem is string host)
        {
            _cookieStore.Revoke(host);
            RefreshCookieList();
        }
    }

    private void ApplyPolicy()
    {
        if (_policyCombo.SelectedItem is SecurityProfile profile)
        {
            _policy.SetProfile(profile);
            switch (profile)
            {
                case SecurityProfile.Relaxed:
                    _policy.ApplyOverrides(new SecurityPolicyOverrides
                    {
                        AllowCookieRestore = true,
                        AllowDevToolsCookieOperations = true,
                        StrictTls = false,
                        DisableThirdPartyCookies = false,
                        MaxLoginDurationSeconds = 3600,
                    });
                    break;
                case SecurityProfile.Strict:
                    _policy.ApplyOverrides(new SecurityPolicyOverrides
                    {
                        AllowCookieRestore = false,
                        AllowDevToolsCookieOperations = false,
                        StrictTls = true,
                        DisableThirdPartyCookies = true,
                        MaxLoginDurationSeconds = 900,
                    });
                    break;
                default:
                    _policy.ApplyOverrides(new SecurityPolicyOverrides
                    {
                        AllowCookieRestore = true,
                        AllowDevToolsCookieOperations = false,
                        StrictTls = true,
                        DisableThirdPartyCookies = true,
                        MaxLoginDurationSeconds = 1800,
                    });
                    break;
            }

            _auditLog.RecordPolicyOverride("global", profile.ToString());
        }
    }

    private void RevalidateIntegrity()
    {
        if (_manifest is null)
        {
            UpdateIntegrityStatus("manifesto não disponível");
            return;
        }

        try
        {
            _integrity.Validate(_manifest);
            UpdateIntegrityStatus("ok");
        }
        catch (IntegrityViolationException ex)
        {
            UpdateIntegrityStatus("falha");
            MessageBox.Show(this, ex.Message, "Integridade", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateIntegrityStatus(string status)
    {
        _integrityStatus.Text = $"Integridade: {status}";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cleanupTimer.Stop();
            _cleanupTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
