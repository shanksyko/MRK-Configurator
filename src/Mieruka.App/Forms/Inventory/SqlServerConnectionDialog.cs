#nullable enable
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;

namespace Mieruka.App.Forms.Inventory;

/// <summary>
/// Diálogo para configurar conexão com SQL Server remoto.
/// </summary>
internal sealed class SqlServerConnectionDialog : Form
{
    private readonly TextBox _txtServer = new();
    private readonly TextBox _txtDatabase = new();
    private readonly RadioButton _radWindows = new();
    private readonly RadioButton _radSqlServer = new();
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new();
    private readonly Button _btnTest = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnOk = new();
    private readonly Button _btnCancel = new();

    private bool _connectionTested;

    /// <summary>
    /// Connection string montada a partir dos campos do diálogo.
    /// Disponível após DialogResult.OK.
    /// </summary>
    public string? ConnectionString { get; private set; }

    public SqlServerConnectionDialog()
    {
        Text = "Conectar ao SQL Server";
        MinimumSize = new Size(460, 360);
        ClientSize = new Size(480, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        // Server
        _txtServer.Dock = DockStyle.Fill;
        _txtServer.TextChanged += (_, _) => OnFieldChanged();
        AddRow(layout, "Servidor:", _txtServer);

        // Database
        _txtDatabase.Dock = DockStyle.Fill;
        _txtDatabase.TextChanged += (_, _) => OnFieldChanged();
        AddRow(layout, "Banco de dados:", _txtDatabase);

        // Auth mode — Windows
        _radWindows.Text = "Autenticação do Windows";
        _radWindows.AutoSize = true;
        _radWindows.Checked = true;
        _radWindows.CheckedChanged += (_, _) => OnAuthModeChanged();
        AddRowSpan(layout, _radWindows);

        // Auth mode — SQL Server
        _radSqlServer.Text = "Autenticação do SQL Server";
        _radSqlServer.AutoSize = true;
        _radSqlServer.CheckedChanged += (_, _) => OnAuthModeChanged();
        AddRowSpan(layout, _radSqlServer);

        // Username
        _txtUsername.Dock = DockStyle.Fill;
        _txtUsername.Enabled = false;
        _txtUsername.TextChanged += (_, _) => OnFieldChanged();
        AddRow(layout, "Usuário:", _txtUsername);

        // Password
        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.Enabled = false;
        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.TextChanged += (_, _) => OnFieldChanged();
        AddRow(layout, "Senha:", _txtPassword);

        // Test connection button + status label
        var testPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 4),
        };

        _btnTest.Text = "Testar Conexão";
        _btnTest.AutoSize = true;
        _btnTest.FlatStyle = FlatStyle.Flat;
        _btnTest.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 215);
        _btnTest.ForeColor = Color.FromArgb(0, 120, 215);
        _btnTest.Click += async (_, _) => await OnTestConnectionAsync();
        testPanel.Controls.Add(_btnTest);

        _lblStatus.AutoSize = true;
        _lblStatus.Margin = new Padding(8, 5, 0, 0);
        _lblStatus.ForeColor = Color.FromArgb(100, 100, 100);
        testPanel.Controls.Add(_lblStatus);

        AddRowSpan(layout, testPanel);

        // Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        _btnCancel.Text = "Cancelar";
        _btnCancel.AutoSize = false;
        _btnCancel.Size = new Size(90, 30);
        _btnCancel.FlatStyle = FlatStyle.Flat;
        _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnOk.BackColor = Color.FromArgb(0, 120, 215);
        _btnOk.FlatStyle = FlatStyle.Flat;
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.ForeColor = Color.White;
        _btnOk.Text = "OK";
        _btnOk.AutoSize = false;
        _btnOk.Size = new Size(90, 30);
        _btnOk.UseVisualStyleBackColor = false;
        _btnOk.Enabled = false;
        _btnOk.Click += OnOkClicked;

        buttonPanel.Controls.Add(_btnCancel);
        buttonPanel.Controls.Add(_btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private void OnAuthModeChanged()
    {
        var sqlAuth = _radSqlServer.Checked;
        _txtUsername.Enabled = sqlAuth;
        _txtPassword.Enabled = sqlAuth;
        OnFieldChanged();
    }

    private void OnFieldChanged()
    {
        _connectionTested = false;
        _btnOk.Enabled = false;
        _lblStatus.Text = string.Empty;
    }

    private string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _txtServer.Text.Trim(),
            InitialCatalog = _txtDatabase.Text.Trim(),
            ConnectTimeout = 10,
            TrustServerCertificate = true,
        };

        if (_radWindows.Checked)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = _txtUsername.Text.Trim();
            builder.Password = _txtPassword.Text;
        }

        return builder.ConnectionString;
    }

    private async Task OnTestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtServer.Text) || string.IsNullOrWhiteSpace(_txtDatabase.Text))
        {
            _lblStatus.ForeColor = Color.FromArgb(200, 60, 60);
            _lblStatus.Text = "Preencha servidor e banco de dados.";
            return;
        }

        _btnTest.Enabled = false;
        _lblStatus.ForeColor = Color.FromArgb(100, 100, 100);
        _lblStatus.Text = "Testando...";

        try
        {
            var connStr = BuildConnectionString();
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            _lblStatus.ForeColor = Color.FromArgb(0, 140, 0);
            _lblStatus.Text = "Conexão bem-sucedida.";
            _connectionTested = true;
            _btnOk.Enabled = true;
        }
        catch (Exception ex)
        {
            _lblStatus.ForeColor = Color.FromArgb(200, 60, 60);
            _lblStatus.Text = $"Falha: {ex.Message}";
            _connectionTested = false;
            _btnOk.Enabled = false;
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }

    private void OnOkClicked(object? sender, EventArgs e)
    {
        if (!_connectionTested)
        {
            MessageBox.Show("Teste a conexão antes de confirmar.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ConnectionString = BuildConnectionString();
        DialogResult = DialogResult.OK;
        Close();
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

    private static void AddRowSpan(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, 2);
    }
}
