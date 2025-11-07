#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Preview;

namespace Mieruka.App.Ui;

internal sealed partial class PreviewForm : WinForms.Form
{
    private readonly List<MonitorInfo> _monitors = new();
    private Func<IMonitorCapture>? _captureFactory;
    private IMonitorCapture? _activeCapture;
    private readonly object _captureGate = new();
    private Drawing.Bitmap? _currentFrame;

    public PreviewForm()
    {
        InitializeComponent();
        _captureFactory = () => new GdiMonitorCaptureProvider();
        CarregarMonitores();
    }

    public PreviewForm(IEnumerable<MonitorInfo> monitors)
        : this()
    {
        ArgumentNullException.ThrowIfNull(monitors);
        CarregarMonitores(monitors);
    }

    protected override void OnFormClosing(WinForms.FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        StopCaptureAsync().GetAwaiter().GetResult();
        LiberarFrameAtual();
    }

    private void CarregarMonitores()
    {
        var monitors = WinForms.Screen.AllScreens.Select(screen => new MonitorInfo
        {
            Key = new MonitorKey { DisplayIndex = Array.IndexOf(WinForms.Screen.AllScreens, screen) },
            Name = string.IsNullOrWhiteSpace(screen.DeviceName) ? screen.FriendlyName() : screen.DeviceName,
            DeviceName = screen.DeviceName,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height,
            Bounds = screen.Bounds,
            WorkArea = screen.WorkingArea,
            Orientation = MonitorOrientation.Unknown,
            Rotation = 0,
            IsPrimary = screen.Primary,
        });

        CarregarMonitores(monitors);
    }

    private void CarregarMonitores(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        _monitors.Clear();
        cmbMonitores.Items.Clear();

        foreach (var info in monitors)
        {
            _monitors.Add(info);
            cmbMonitores.Items.Add(new MonitorOption(info));
        }

        if (cmbMonitores.Items.Count > 0)
        {
            cmbMonitores.SelectedIndex = 0;
        }
        else
        {
            AtualizarStatus("Nenhum monitor detectado.");
        }
    }

    private async void btnIniciar_Click(object? sender, EventArgs e)
    {
        await IniciarCapturaAsync().ConfigureAwait(false);
    }

    private async Task IniciarCapturaAsync()
    {
        await StopCaptureAsync().ConfigureAwait(false);

        if (cmbMonitores.SelectedItem is not MonitorOption option)
        {
            AtualizarStatus("Selecione um monitor para iniciar a captura.");
            return;
        }

        if (_captureFactory is null)
        {
            AtualizarStatus("Nenhum provedor de captura disponível.");
            return;
        }

        IMonitorCapture? capture = null;

        try
        {
            capture = _captureFactory();
            if (!capture.IsSupported)
            {
                throw new PlatformNotSupportedException("O provedor selecionado não é suportado neste sistema.");
            }

            capture.FrameArrived += OnFrameArrived;
            await capture.StartAsync(option.Info).ConfigureAwait(false);

            lock (_captureGate)
            {
                _activeCapture = capture;
            }

            AtualizarStatus($"Capturando monitor '{option.DisplayName}'.");
        }
        catch (Exception ex)
        {
            AtualizarStatus($"Falha ao iniciar captura: {ex.Message}");
            if (capture is not null)
            {
                capture.FrameArrived -= OnFrameArrived;
                await capture.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async void btnParar_Click(object? sender, EventArgs e)
    {
        await StopCaptureAsync().ConfigureAwait(false);
        AtualizarStatus("Captura interrompida.");
    }

    private void btnCapturaGdi_Click(object? sender, EventArgs e)
    {
        _captureFactory = () => new GdiMonitorCaptureProvider();
        AtualizarStatus("Modo de captura GDI selecionado.");
    }

    private async void btnCapturaGpu_Click(object? sender, EventArgs e)
    {
        try
        {
            await using var probe = new GraphicsCaptureProvider();
            if (!probe.IsSupported)
            {
                throw new PlatformNotSupportedException("Captura por GPU não suportada neste sistema.");
            }
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, $"Captura GPU indisponível: {ex.Message}", "Preview", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        _captureFactory = () => new GraphicsCaptureProvider();
        AtualizarStatus("Modo de captura GPU selecionado.");
    }

    private async Task StopCaptureAsync()
    {
        IMonitorCapture? capture;
        lock (_captureGate)
        {
            capture = _activeCapture;
            _activeCapture = null;
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
        catch
        {
            // Ignore errors when stopping the capture.
        }

        await capture.DisposeAsync().ConfigureAwait(false);
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        Drawing.Bitmap? frame = null;
        try
        {
            var source = e.Frame;
            var bounds = new Drawing.Rectangle(0, 0, source.Width, source.Height);
            frame = source.Clone(bounds, source.PixelFormat);
        }
        catch
        {
            // Ignore frame cloning errors.
        }
        finally
        {
            e.Dispose();
        }

        if (frame is null)
        {
            return;
        }

        BeginInvoke(new Action(() => ExibirFrame(frame)));
    }

    private void ExibirFrame(Drawing.Bitmap frame)
    {
        LiberarFrameAtual();
        _currentFrame = frame;
        picPreview.Image = frame;
    }

    private void LiberarFrameAtual()
    {
        if (_currentFrame is not null)
        {
            picPreview.Image = null;
            _currentFrame.Dispose();
            _currentFrame = null;
        }
    }

    private void AtualizarStatus(string mensagem)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AtualizarStatus), mensagem);
            return;
        }

        lblStatus.Text = mensagem;
    }

    private sealed class MonitorOption
    {
        public MonitorOption(MonitorInfo info)
        {
            Info = info;
            DisplayName = string.IsNullOrWhiteSpace(info.Name)
                ? $"Monitor {info.Key.DisplayIndex + 1}"
                : info.Name;
        }

        public MonitorInfo Info { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}

internal static class ScreenExtensions
{
    public static string FriendlyName(this WinForms.Screen screen)
    {
        return string.IsNullOrWhiteSpace(screen.DeviceName)
            ? "Monitor"
            : screen.DeviceName;
    }
}
