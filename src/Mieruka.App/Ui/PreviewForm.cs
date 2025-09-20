using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Preview;
using Serilog;

namespace Mieruka.App.Ui;

/// <summary>
/// Window that renders a live monitor preview using the capture subsystem.
/// </summary>
internal sealed class PreviewForm : Form
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly ComboBox _monitorSelector;
    private readonly PictureBox _previewBox;
    private readonly Label _statusLabel;
    private IMonitorCapture? _capture;
    private readonly object _captureGate = new();

    public PreviewForm(IReadOnlyList<MonitorInfo> monitors)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));

        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Preview de Monitores";
        ClientSize = new Size(960, 600);

        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        _monitorSelector = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = baseFont,
            Margin = new Padding(12),
        };

        _previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Padding = new Padding(12),
            Font = baseFont,
            Text = "Selecione um monitor para iniciar o preview.",
        };

        Controls.Add(_previewBox);
        Controls.Add(_monitorSelector);
        Controls.Add(_statusLabel);

        _monitorSelector.SelectedIndexChanged += async (_, _) => await RestartCaptureAsync().ConfigureAwait(false);

        PopulateMonitors();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_monitorSelector.SelectedIndex < 0 && _monitorSelector.Items.Count > 0)
        {
            _monitorSelector.SelectedIndex = 0;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        StopCaptureAsync().GetAwaiter().GetResult();
        DisposeCurrentFrame();
    }

    private void PopulateMonitors()
    {
        _monitorSelector.Items.Clear();

        foreach (var monitor in _monitors)
        {
            var name = string.IsNullOrWhiteSpace(monitor.Name)
                ? $"Monitor {monitor.Key.DisplayIndex + 1}"
                : monitor.Name;
            _monitorSelector.Items.Add(new MonitorOption(name, monitor));
        }

        if (_monitorSelector.Items.Count > 0)
        {
            _monitorSelector.SelectedIndex = 0;
        }
        else
        {
            _monitorSelector.Enabled = false;
            UpdateStatus("Nenhum monitor disponível para preview.");
        }
    }

    private async Task RestartCaptureAsync()
    {
        await StopCaptureAsync().ConfigureAwait(false);

        if (_monitorSelector.SelectedItem is not MonitorOption option)
        {
            return;
        }

        IMonitorCapture? capture = null;

        try
        {
            Log.Information("Iniciando preview para {MonitorName}.", option.DisplayName);
            capture = MonitorCaptureFactory.Create();
            capture.FrameArrived += OnFrameArrived;
            await capture.StartAsync(option.Monitor).ConfigureAwait(false);

            lock (_captureGate)
            {
                _capture = capture;
            }

            UpdateStatus($"Preview ativo: {option.DisplayName}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao iniciar preview para {MonitorName}.", option.DisplayName);
            UpdateStatus($"Falha ao iniciar preview: {ex.Message}");
            if (capture is not null)
            {
                capture.FrameArrived -= OnFrameArrived;
                await capture.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task StopCaptureAsync()
    {
        IMonitorCapture? capture;
        lock (_captureGate)
        {
            capture = _capture;
            _capture = null;
        }

        if (capture is null)
        {
            return;
        }

        capture.FrameArrived -= OnFrameArrived;

        try
        {
            await capture.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Erro ao interromper o preview.");
        }

        await capture.DisposeAsync().ConfigureAwait(false);
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        Bitmap? frame = null;
        try
        {
            var source = e.Bitmap;
            var bounds = new Rectangle(0, 0, source.Width, source.Height);
            frame = source.Clone(bounds, source.PixelFormat);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao clonar frame do preview.");
        }
        finally
        {
            e.Dispose();
        }

        if (frame is null)
        {
            return;
        }

        BeginInvoke(new Action(() => DisplayFrame(frame)));
    }

    private void DisplayFrame(Bitmap bitmap)
    {
        var previous = _previewBox.Image as Bitmap;
        _previewBox.Image = bitmap;
        previous?.Dispose();
    }

    private void DisposeCurrentFrame()
    {
        if (_previewBox.Image is Bitmap bitmap)
        {
            _previewBox.Image = null;
            bitmap.Dispose();
        }
    }

    private void UpdateStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(UpdateStatus), message);
            return;
        }

        _statusLabel.Text = message;
    }

    private sealed record class MonitorOption(string DisplayName, MonitorInfo Monitor)
    {
        public override string ToString() => DisplayName;
    }
}
