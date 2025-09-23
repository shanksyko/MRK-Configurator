#nullable disable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms;

partial class MainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private IContainer? components = null;

    internal DataGridView dgvSites = null!;
    internal BindingSource bsSites = null!;
    internal Button btnAdicionar = null!;
    internal Button btnEditar = null!;
    internal Button btnClonar = null!;
    internal Button btnRemover = null!;
    internal Button btnTestar = null!;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new Container();
        dgvSites = new DataGridView();
        bsSites = new BindingSource(components);
        var colId = new DataGridViewTextBoxColumn();
        var colUrl = new DataGridViewTextBoxColumn();
        var layout = new TableLayoutPanel();
        var buttonPanel = new FlowLayoutPanel();
        btnAdicionar = new Button();
        btnEditar = new Button();
        btnClonar = new Button();
        btnRemover = new Button();
        btnTestar = new Button();
        ((ISupportInitialize)dgvSites).BeginInit();
        ((ISupportInitialize)bsSites).BeginInit();
        SuspendLayout();
        // 
        // dgvSites
        // 
        dgvSites.AllowUserToAddRows = false;
        dgvSites.AllowUserToDeleteRows = false;
        dgvSites.AutoGenerateColumns = false;
        dgvSites.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvSites.Columns.AddRange(new DataGridViewColumn[] { colId, colUrl });
        dgvSites.DataSource = bsSites;
        dgvSites.Dock = DockStyle.Fill;
        dgvSites.MultiSelect = false;
        dgvSites.ReadOnly = true;
        dgvSites.RowHeadersVisible = false;
        dgvSites.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        // 
        // colId
        // 
        colId.DataPropertyName = nameof(Mieruka.Core.Models.SiteConfig.Id);
        colId.HeaderText = "SiteId";
        colId.MinimumWidth = 120;
        colId.Name = "colId";
        colId.ReadOnly = true;
        colId.Width = 160;
        // 
        // colUrl
        // 
        colUrl.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        colUrl.DataPropertyName = nameof(Mieruka.Core.Models.SiteConfig.Url);
        colUrl.HeaderText = "URL";
        colUrl.MinimumWidth = 200;
        colUrl.Name = "colUrl";
        colUrl.ReadOnly = true;
        // 
        // layout
        // 
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(dgvSites, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        layout.Dock = DockStyle.Fill;
        layout.Location = new System.Drawing.Point(0, 0);
        layout.Name = "layout";
        layout.RowCount = 2;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Size = new System.Drawing.Size(984, 661);
        layout.TabIndex = 0;
        // 
        // buttonPanel
        // 
        buttonPanel.AutoSize = true;
        buttonPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Padding = new Padding(12);
        buttonPanel.WrapContents = false;
        buttonPanel.Controls.Add(btnTestar);
        buttonPanel.Controls.Add(btnRemover);
        buttonPanel.Controls.Add(btnClonar);
        buttonPanel.Controls.Add(btnEditar);
        buttonPanel.Controls.Add(btnAdicionar);
        // 
        // btnAdicionar
        // 
        btnAdicionar.AutoSize = true;
        btnAdicionar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAdicionar.Margin = new Padding(6, 3, 0, 3);
        btnAdicionar.Name = "btnAdicionar";
        btnAdicionar.Size = new System.Drawing.Size(73, 25);
        btnAdicionar.TabIndex = 0;
        btnAdicionar.Text = "Adicionar";
        btnAdicionar.UseVisualStyleBackColor = true;
        btnAdicionar.Click += btnAdicionar_Click;
        // 
        // btnEditar
        // 
        btnEditar.AutoSize = true;
        btnEditar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnEditar.Margin = new Padding(6, 3, 0, 3);
        btnEditar.Name = "btnEditar";
        btnEditar.Size = new System.Drawing.Size(50, 25);
        btnEditar.TabIndex = 1;
        btnEditar.Text = "Editar";
        btnEditar.UseVisualStyleBackColor = true;
        btnEditar.Click += btnEditar_Click;
        // 
        // btnClonar
        // 
        btnClonar.AutoSize = true;
        btnClonar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnClonar.Margin = new Padding(6, 3, 0, 3);
        btnClonar.Name = "btnClonar";
        btnClonar.Size = new System.Drawing.Size(52, 25);
        btnClonar.TabIndex = 2;
        btnClonar.Text = "Clonar";
        btnClonar.UseVisualStyleBackColor = true;
        btnClonar.Click += btnClonar_Click;
        // 
        // btnRemover
        // 
        btnRemover.AutoSize = true;
        btnRemover.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnRemover.Margin = new Padding(6, 3, 0, 3);
        btnRemover.Name = "btnRemover";
        btnRemover.Size = new System.Drawing.Size(64, 25);
        btnRemover.TabIndex = 3;
        btnRemover.Text = "Remover";
        btnRemover.UseVisualStyleBackColor = true;
        btnRemover.Click += btnRemover_Click;
        // 
        // btnTestar
        // 
        btnTestar.AutoSize = true;
        btnTestar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestar.Margin = new Padding(6, 3, 0, 3);
        btnTestar.Name = "btnTestar";
        btnTestar.Size = new System.Drawing.Size(92, 25);
        btnTestar.TabIndex = 4;
        btnTestar.Text = "Testar Login";
        btnTestar.UseVisualStyleBackColor = true;
        btnTestar.Click += btnTestar_Click;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(984, 661);
        Controls.Add(layout);
        MinimumSize = new System.Drawing.Size(900, 600);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Mieruka Configurator";
        ((ISupportInitialize)dgvSites).EndInit();
        ((ISupportInitialize)bsSites).EndInit();
        ResumeLayout(false);
    }

    #endregion
}
