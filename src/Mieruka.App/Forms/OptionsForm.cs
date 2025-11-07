using System;
using System.Drawing;
using System.Windows.Forms;
using ConfigAppConfig = Mieruka.Core.Config.AppConfig;

namespace Mieruka.App.Forms;

public sealed class OptionsForm : Form
{
    private readonly ConfigAppConfig _config;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public OptionsForm(ConfigAppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        Text = "Opções";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(360, 200);

        var infoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 120,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Configurações adicionais estarão disponíveis em breve.",
            Padding = new Padding(16)
        };

        _okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Size = new Size(94, 29)
        };

        _cancelButton = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Size = new Size(94, 29)
        };

        Controls.Add(infoLabel);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        LayoutButtons();
        Resize += (_, _) => LayoutButtons();
    }

    private void LayoutButtons()
    {
        var margin = 20;
        _cancelButton.Location = new Point(ClientSize.Width - _cancelButton.Width - margin, ClientSize.Height - _cancelButton.Height - margin);
        _okButton.Location = new Point(_cancelButton.Left - _okButton.Width - 10, _cancelButton.Top);
    }
}
