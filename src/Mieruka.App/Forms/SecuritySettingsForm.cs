using System;
using System.Linq;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;
using Mieruka.Core.Security;
using Mieruka.Core.Security.Policy;

namespace Mieruka.App.Forms;

/// <summary>
/// Provides a configuration surface for security sensitive settings.
/// </summary>
public sealed class SecuritySettingsForm : Forms.Form
{
    private readonly UrlAllowlist _allowlist;
    private readonly SecurityPolicy _policy;
    private readonly CookieSafeStore _cookieStore;
    private readonly IntegrityService _integrity;
    private readonly AuditLog _auditLog;
    private readonly IntegrityManifest? _manifest;

    private readonly Forms.ComboBox _policyCombo;
    private readonly Forms.ListBox _allowListBox;
    private readonly Forms.TextBox _hostInput;
    private readonly Forms.ListBox _cookieHosts;
    private readonly Forms.CheckBox _telemetryOptIn;
    private readonly Forms.Label _integrityStatus;
    private readonly WinTimer _cleanupTimer;

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
        StartPosition = Forms.FormStartPosition.CenterParent;
        Size = new Drawing.Size(640, 480);
        MinimumSize = new Drawing.Size(600, 420);

        var layout = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Forms.Padding(8),
            AutoSize = true,
        };

        layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));
        layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 50));

        // Policy selector
        var policyLabel = new Forms.Label
        {
            Text = "Perfil de política",
            Dock = Forms.DockStyle.Fill,
            AutoSize = true,
        };
        _policyCombo = new Forms.ComboBox
        {
            Dock = Forms.DockStyle.Fill,
            DropDownStyle = Forms.ComboBoxStyle.DropDownList,
            DataSource = Enum.GetValues(typeof(SecurityProfile)),
        };
        _policyCombo.SelectedItem = _policy.Profile;
        _policyCombo.SelectedIndexChanged += (_, _) => ApplyPolicy();

        layout.Controls.Add(policyLabel, 0, 0);
        layout.Controls.Add(_policyCombo, 1, 0);

        // Allowlist controls
        _allowListBox = new Forms.ListBox { Dock = Forms.DockStyle.Fill };
        _hostInput = new Forms.TextBox { Dock = Forms.DockStyle.Fill, PlaceholderText = "Adicionar host" };
        var addHostButton = new Forms.Button { Text = "Adicionar", Dock = Forms.DockStyle.Fill };
        var removeHostButton = new Forms.Button { Text = "Remover", Dock = Forms.DockStyle.Fill };

        addHostButton.Click += (_, _) => AddAllowlistEntry();
        removeHostButton.Click += (_, _) => RemoveAllowlistEntry();

        var allowListPanel = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
        };
        allowListPanel.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 70));
        allowListPanel.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 30));
        allowListPanel.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
        allowListPanel.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
        allowListPanel.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));

        allowListPanel.Controls.Add(_hostInput, 0, 0);
        allowListPanel.Controls.Add(addHostButton, 1, 0);
        allowListPanel.SetColumnSpan(_allowListBox, 2);
        allowListPanel.Controls.Add(_allowListBox, 0, 1);
        allowListPanel.Controls.Add(removeHostButton, 1, 2);

        var allowListGroup = new Forms.GroupBox
        {
            Text = "Hosts permitidos",
            Dock = Forms.DockStyle.Fill,
        };
        allowListGroup.Controls.Add(allowListPanel);

        layout.Controls.Add(allowListGroup, 0, 1);
        layout.SetColumnSpan(allowListGroup, 2);

        // Cookie management
        _cookieHosts = new Forms.ListBox { Dock = Forms.DockStyle.Fill };
        var revokeButton = new Forms.Button { Text = "Revogar cookies", Dock = Forms.DockStyle.Fill };
        revokeButton.Click += (_, _) => RevokeSelectedCookie();

        var cookieGroup = new Forms.GroupBox
        {
            Text = "Cookies armazenados",
            Dock = Forms.DockStyle.Fill,
        };
        var cookiePanel = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        cookiePanel.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
        cookiePanel.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
        cookiePanel.Controls.Add(_cookieHosts, 0, 0);
        cookiePanel.Controls.Add(revokeButton, 0, 1);
        cookieGroup.Controls.Add(cookiePanel);

        layout.Controls.Add(cookieGroup, 0, 2);
        layout.SetColumnSpan(cookieGroup, 2);

        // Telemetry options
        _telemetryOptIn = new Forms.CheckBox
        {
            Text = "Permitir telemetria anônima",
            Dock = Forms.DockStyle.Fill,
        };
        _telemetryOptIn.CheckedChanged += (_, _) => _auditLog.WriteEvent(new AuditLog.AuditEvent("telemetry")
        {
            Result = _telemetryOptIn.Checked ? "enabled" : "disabled",
        });

        layout.Controls.Add(_telemetryOptIn, 0, 3);
        layout.SetColumnSpan(_telemetryOptIn, 2);

        // Integrity status
        _integrityStatus = new Forms.Label
        {
            Text = "Integridade: não verificada",
            Dock = Forms.DockStyle.Fill,
            AutoSize = true,
        };
        var revalidateButton = new Forms.Button
        {
            Text = "Revalidar",
            Dock = Forms.DockStyle.Right,
        };
        revalidateButton.Click += (_, _) => RevalidateIntegrity();

        layout.Controls.Add(_integrityStatus, 0, 4);
        layout.Controls.Add(revalidateButton, 1, 4);

        Controls.Add(layout);

        Load += (_, _) => RefreshData();

        _cleanupTimer = new WinTimer { Interval = (int)TimeSpan.FromMinutes(15).TotalMilliseconds };
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
            Forms.MessageBox.Show(this, ex.Message, "Host inválido", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
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
            Forms.MessageBox.Show(this, ex.Message, "Integridade", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);
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
