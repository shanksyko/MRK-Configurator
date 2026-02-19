#nullable enable
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms;

/// <summary>
/// Editor for configuring scheduled orchestrator start/stop times.
/// </summary>
internal sealed class ScheduleEditorForm : Form
{
    private readonly CheckBox _chkEnabled;
    private readonly DateTimePicker _dtpStart;
    private readonly DateTimePicker _dtpStop;
    private readonly CheckedListBox _clbDays;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    public ScheduleConfig Result { get; private set; } = new();

    public ScheduleEditorForm(ScheduleConfig? current = null)
    {
        Text = "Agendamento de Execução";
        Size = new Size(400, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _chkEnabled = new CheckBox
        {
            Text = "Agendamento habilitado",
            AutoSize = true,
            Checked = current?.Enabled ?? false,
        };
        layout.SetColumnSpan(_chkEnabled, 2);
        layout.Controls.Add(_chkEnabled, 0, 0);

        layout.Controls.Add(new Label
        {
            Text = "Início:",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
        }, 0, 1);

        _dtpStart = new DateTimePicker
        {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true,
            Dock = DockStyle.Fill,
        };
        if (current?.StartTime is not null)
        {
            _dtpStart.Value = DateTime.Today.Add(current.StartTime.Value.ToTimeSpan());
        }
        layout.Controls.Add(_dtpStart, 1, 1);

        layout.Controls.Add(new Label
        {
            Text = "Fim:",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
        }, 0, 2);

        _dtpStop = new DateTimePicker
        {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true,
            Dock = DockStyle.Fill,
        };
        if (current?.StopTime is not null)
        {
            _dtpStop.Value = DateTime.Today.Add(current.StopTime.Value.ToTimeSpan());
        }
        layout.Controls.Add(_dtpStop, 1, 2);

        layout.Controls.Add(new Label
        {
            Text = "Dias:",
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = true,
        }, 0, 3);

        _clbDays = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
        };
        var dayNames = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
        for (int i = 0; i < dayNames.Length; i++)
        {
            var dayOfWeek = (DayOfWeek)i;
            var isChecked = current?.DaysOfWeek.Contains(dayOfWeek) ?? false;
            _clbDays.Items.Add(dayNames[i], isChecked);
        }
        layout.Controls.Add(_clbDays, 1, 3);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };
        layout.SetColumnSpan(buttonPanel, 2);
        layout.Controls.Add(buttonPanel, 0, 4);

        _btnCancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(_btnCancel);

        _btnOk = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
        _btnOk.Click += (_, _) => BuildResult();
        buttonPanel.Controls.Add(_btnOk);

        Controls.Add(layout);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private void BuildResult()
    {
        var days = new System.Collections.Generic.List<DayOfWeek>();
        for (int i = 0; i < _clbDays.Items.Count; i++)
        {
            if (_clbDays.GetItemChecked(i))
            {
                days.Add((DayOfWeek)i);
            }
        }

        Result = new ScheduleConfig
        {
            Enabled = _chkEnabled.Checked,
            StartTime = TimeOnly.FromDateTime(_dtpStart.Value),
            StopTime = TimeOnly.FromDateTime(_dtpStop.Value),
            DaysOfWeek = days,
        };
    }
}
