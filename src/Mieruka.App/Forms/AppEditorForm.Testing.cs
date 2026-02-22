#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Interop;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
  private async void btnTestarJanela_Click(object? sender, EventArgs e)
  {
    if (!OperatingSystem.IsWindows())
    {
      WinForms.MessageBox.Show(
          this,
          "O teste de posicionamento está disponível apenas no Windows.",
          "Teste de janela",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return;
    }

    var monitor = GetSelectedMonitor();
    if (monitor is null)
    {
      WinForms.MessageBox.Show(
          this,
          "Selecione um monitor para testar a posição.",
          "Teste de janela",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return;
    }

    var selectedApp = await PickInstalledAppForTestAsync().ConfigureAwait(true);
    if (selectedApp is null)
    {
      return;
    }

    var window = BuildWindowConfigurationFromInputs();
    if (!window.FullScreen)
    {
      window = ClampWindowBounds(window, monitor);
    }

    window = window with { Monitor = monitor.Key };
    var button = btnTestarJanela;

    if (button is not null)
    {
      button.Enabled = false;
    }

    try
    {
      await LaunchAppForWindowTestAsync(monitor, window, selectedApp).ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      _logger.Error("Falha ao testar a posição com aplicativo selecionado.", ex);
      WinForms.MessageBox.Show(
          this,
          $"Não foi possível testar a posição: {ex.Message}",
          "Teste de janela",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Error);
    }
    finally
    {
      if (button is not null)
      {
        button.Enabled = true;
      }
    }
  }

  partial void OnBeforeMoveWindowUI();

  private async void btnTestReal_Click(object? sender, EventArgs e)
  {
    var button = btnTestReal;
    if (button is not null)
    {
      if (!button.Enabled)
      {
        return;
      }

      button.Enabled = false;
    }

    try
    {
      UseWaitCursor = true;
      WinForms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;

      try
      {
        await SuspendPreviewCaptureAsync().ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Falha ao suspender pré-visualização antes de testar app real.");
      }
      OnBeforeMoveWindowUI();

      var app = ConstruirPrograma();
      await _appTestRunner.RunTestAsync(app, this).ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      _logger.Error("Falha ao executar o aplicativo real durante o teste.", ex);
      WinForms.MessageBox.Show(
          this,
          $"Não foi possível executar o aplicativo real: {ex.Message}",
          "Teste de aplicativo",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Error);
    }
    finally
    {
      WinForms.Cursor.Current = System.Windows.Forms.Cursors.Default;
      SchedulePreviewResume();
      UseWaitCursor = false;

      if (button is not null)
      {
        button.Enabled = true;
      }
    }
  }

  private bool TryBuildCurrentApp(
      string messageTitle,
      out ProgramaConfig app,
      out MonitorInfo monitor,
      out Drawing.Rectangle bounds,
      string? executableOverride = null,
      string? argumentsOverride = null,
      bool requireExecutable = true)
  {
    app = default!;
    monitor = default!;
    bounds = default;

    var selectedMonitor = GetSelectedMonitor();
    if (selectedMonitor is null)
    {
      WinForms.MessageBox.Show(
          this,
          "Selecione um monitor para testar a posição.",
          messageTitle,
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return false;
    }

    var config = ConstruirPrograma();

    if (executableOverride is not null)
    {
      var overridePath = executableOverride.Trim();
      config = config with { ExecutablePath = overridePath };
    }

    if (argumentsOverride is not null)
    {
      var overrideArguments = string.IsNullOrWhiteSpace(argumentsOverride)
          ? null
          : argumentsOverride;
      config = config with { Arguments = overrideArguments };
    }

    var executablePath = config.ExecutablePath;
    if (requireExecutable && string.IsNullOrWhiteSpace(executablePath))
    {
      WinForms.MessageBox.Show(
          this,
          "Informe um executável válido antes de executar o teste.",
          messageTitle,
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return false;
    }

    if (requireExecutable && !string.IsNullOrWhiteSpace(executablePath) && !File.Exists(executablePath))
    {
      WinForms.MessageBox.Show(
          this,
          "Executável não encontrado. Ajuste o caminho antes de executar o teste.",
          messageTitle,
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return false;
    }

    var resolvedBounds = WindowPlacementHelper.ResolveBounds(config.Window, selectedMonitor);

    app = config;
    monitor = selectedMonitor;
    bounds = resolvedBounds;
    return true;
  }

  private WindowConfig BuildWindowConfigurationFromInputs()
  {
    if (chkJanelaTelaCheia.Checked)
    {
      return new WindowConfig { FullScreen = true };
    }

    return new WindowConfig
    {
      FullScreen = false,
      X = (int)nudJanelaX.Value,
      Y = (int)nudJanelaY.Value,
      Width = (int)nudJanelaLargura.Value,
      Height = (int)nudJanelaAltura.Value,
    };
  }

  private static void LaunchExecutable(string executablePath, string? arguments)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = executablePath,
      UseShellExecute = false,
    };

    if (!string.IsNullOrWhiteSpace(arguments))
    {
      startInfo.Arguments = arguments;
    }

    var workingDirectory = Path.GetDirectoryName(executablePath);
    if (!string.IsNullOrWhiteSpace(workingDirectory))
    {
      startInfo.WorkingDirectory = workingDirectory;
    }

    using var process = Process.Start(startInfo);
    if (process is null)
    {
      throw new InvalidOperationException("Não foi possível iniciar o aplicativo selecionado.");
    }
  }

  private async Task<InstalledAppInfo?> PickInstalledAppForTestAsync()
  {
    IReadOnlyList<InstalledAppInfo> apps;
    try
    {
      UseWaitCursor = true;
      WinForms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
      apps = await _installedAppsProvider.QueryAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Falha ao carregar aplicativos para seleção de teste.");
      WinForms.MessageBox.Show(
          this,
          "Não foi possível carregar a lista de aplicativos instalados.",
          "Teste de janela",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Warning);
      return null;
    }
    finally
    {
      WinForms.Cursor.Current = System.Windows.Forms.Cursors.Default;
      UseWaitCursor = false;
    }

    if (apps.Count == 0)
    {
      WinForms.MessageBox.Show(
          this,
          "Nenhum aplicativo instalado foi encontrado.",
          "Teste de janela",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return null;
    }

    using var dialog = new WinForms.Form
    {
      Text = "Selecionar aplicativo para teste",
      StartPosition = WinForms.FormStartPosition.CenterParent,
      Size = new Drawing.Size(520, 420),
      MinimumSize = new Drawing.Size(400, 300),
      FormBorderStyle = WinForms.FormBorderStyle.Sizable,
      ShowInTaskbar = false,
      MaximizeBox = false,
      MinimizeBox = false,
    };
    DoubleBufferingHelper.EnableOptimizedDoubleBuffering(dialog);

    var searchBox = new WinForms.TextBox
    {
      PlaceholderText = "Buscar aplicativo...",
      Dock = WinForms.DockStyle.Top,
      Margin = new WinForms.Padding(8),
    };

    var listBox = new WinForms.ListBox
    {
      Dock = WinForms.DockStyle.Fill,
      Font = new Drawing.Font("Segoe UI", 10F),
      ItemHeight = 26,
      DrawMode = WinForms.DrawMode.OwnerDrawFixed,
    };

    var allItems = apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    foreach (var app in allItems)
    {
      listBox.Items.Add(app);
    }

    listBox.Format += (_, fmtArgs) =>
    {
      if (fmtArgs.ListItem is InstalledAppInfo appInfo)
      {
        fmtArgs.Value = appInfo.Name;
      }
    };

    listBox.DrawItem += (_, drawArgs) =>
    {
      if (drawArgs.Index < 0)
      {
        return;
      }

      drawArgs.DrawBackground();
      var item = (InstalledAppInfo)listBox.Items[drawArgs.Index];
      var isSelected = (drawArgs.State & WinForms.DrawItemState.Selected) != 0;
      var textColor = isSelected ? Drawing.Color.White : Drawing.Color.FromArgb(33, 33, 33);
      var detailColor = isSelected ? Drawing.Color.FromArgb(220, 220, 220) : Drawing.Color.Gray;

      using var textBrush = new Drawing.SolidBrush(textColor);
      using var detailBrush = new Drawing.SolidBrush(detailColor);
      using var nameFont = new Drawing.Font("Segoe UI", 9.5F, Drawing.FontStyle.Regular);
      using var detailFont = new Drawing.Font("Segoe UI", 7.5F, Drawing.FontStyle.Regular);

      var bounds = drawArgs.Bounds;
      var nameRect = new Drawing.RectangleF(bounds.X + 8, bounds.Y + 2, bounds.Width * 0.5f, bounds.Height - 4);
      var detailText = !string.IsNullOrWhiteSpace(item.Vendor) ? item.Vendor : System.IO.Path.GetFileName(item.ExecutablePath);
      var detailRect = new Drawing.RectangleF(bounds.X + bounds.Width * 0.55f, bounds.Y + 4, bounds.Width * 0.44f, bounds.Height - 4);

      drawArgs.Graphics.DrawString(item.Name, nameFont, textBrush, nameRect, Drawing.StringFormat.GenericDefault);
      drawArgs.Graphics.DrawString(detailText, detailFont, detailBrush, detailRect, Drawing.StringFormat.GenericDefault);
      drawArgs.DrawFocusRectangle();
    };

    searchBox.TextChanged += (_, _) =>
    {
      var term = searchBox.Text?.Trim();
      listBox.BeginUpdate();
      listBox.Items.Clear();
      var filtered = string.IsNullOrWhiteSpace(term)
          ? allItems
          : allItems.Where(a =>
              a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
              (!string.IsNullOrWhiteSpace(a.Vendor) && a.Vendor.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
      foreach (var app in filtered)
      {
        listBox.Items.Add(app);
      }
      listBox.EndUpdate();
    };

    InstalledAppInfo? selectedApp = null;
    var btnOk = new WinForms.Button
    {
      Text = "Selecionar",
      DialogResult = WinForms.DialogResult.OK,
      AutoSize = true,
      Dock = WinForms.DockStyle.Right,
      Margin = new WinForms.Padding(8),
    };

    var btnCancel = new WinForms.Button
    {
      Text = "Cancelar",
      DialogResult = WinForms.DialogResult.Cancel,
      AutoSize = true,
      Dock = WinForms.DockStyle.Right,
      Margin = new WinForms.Padding(8),
    };

    var footerPanel = new WinForms.FlowLayoutPanel
    {
      Dock = WinForms.DockStyle.Bottom,
      FlowDirection = WinForms.FlowDirection.RightToLeft,
      AutoSize = true,
      AutoSizeMode = WinForms.AutoSizeMode.GrowAndShrink,
      Padding = new WinForms.Padding(4),
    };
    footerPanel.Controls.Add(btnOk);
    footerPanel.Controls.Add(btnCancel);

    dialog.AcceptButton = btnOk;
    dialog.CancelButton = btnCancel;
    dialog.Controls.Add(listBox);
    dialog.Controls.Add(searchBox);
    dialog.Controls.Add(footerPanel);

    listBox.DoubleClick += (_, _) =>
    {
      if (listBox.SelectedItem is InstalledAppInfo)
      {
        dialog.DialogResult = WinForms.DialogResult.OK;
        dialog.Close();
      }
    };

    if (dialog.ShowDialog(this) == WinForms.DialogResult.OK && listBox.SelectedItem is InstalledAppInfo chosen)
    {
      selectedApp = chosen;
    }

    return selectedApp;
  }

  private async Task LaunchAppForWindowTestAsync(MonitorInfo monitor, WindowConfig window, InstalledAppInfo appInfo)
  {
    var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);

    // Check if the application is already running before launching a new instance.
    using var existingProcess = AppRunner.FindRunningProcess(appInfo.ExecutablePath);
    if (existingProcess is not null)
    {
      try
      {
        if (!existingProcess.HasExited)
        {
          existingProcess.Refresh();
          var handle = existingProcess.MainWindowHandle;
          if (handle == IntPtr.Zero)
          {
            handle = await WindowWaiter.WaitForMainWindowAsync(existingProcess, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
          }

          try
          {
            await SuspendPreviewCaptureAsync().ConfigureAwait(true);
          }
          catch (Exception ex)
          {
            _logger.Warning(ex, "Falha ao suspender pré-visualização antes do movimento da janela de teste.");
          }
          try
          {
            WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
            User32.SetForegroundWindow(handle);
          }
          finally
          {
            SchedulePreviewResume();
          }
          return;
        }
      }
      catch (Exception ex)
      {
        _logger.Debug(ex, "Falha ao reposicionar processo já em execução; tentando iniciar nova instância.");
      }
    }

    var startInfo = new ProcessStartInfo
    {
      FileName = appInfo.ExecutablePath,
      UseShellExecute = true,
    };

    var workingDirectory = Path.GetDirectoryName(appInfo.ExecutablePath);
    if (!string.IsNullOrWhiteSpace(workingDirectory))
    {
      startInfo.WorkingDirectory = workingDirectory;
    }

    var process = Process.Start(startInfo);
    if (process is null)
    {
      throw new InvalidOperationException($"Não foi possível iniciar \"{appInfo.Name}\".");
    }

    try
    {
      var handle = await WindowWaiter.WaitForMainWindowAsync(process, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
      try
      {
        await SuspendPreviewCaptureAsync().ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Falha ao suspender pré-visualização antes do movimento da janela de teste.");
      }
      try
      {
        WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
        User32.SetForegroundWindow(handle);
      }
      finally
      {
        SchedulePreviewResume();
      }
    }
    finally
    {
      process.Dispose();
    }
  }

  private async Task LaunchDummyWindowAsync(MonitorInfo monitor, WindowConfig window)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "notepad.exe",
      UseShellExecute = true,
    };

    var process = Process.Start(startInfo);
    if (process is null)
    {
      throw new InvalidOperationException("Não foi possível iniciar a janela de teste.");
    }

    try
    {
      var handle = await WindowWaiter.WaitForMainWindowAsync(process, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
      var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);
      try
      {
        await SuspendPreviewCaptureAsync().ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Falha ao suspender pré-visualização antes do movimento da janela de teste.");
      }
      try
      {
        WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
      }
      finally
      {
        SchedulePreviewResume();
      }
      await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
    }
    finally
    {
      try
      {
        if (!process.HasExited)
        {
          process.CloseMainWindow();
          if (!process.WaitForExit((int)WindowTestTimeout.TotalMilliseconds))
          {
            process.Kill(entireProcessTree: true);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.Debug(ex, "Falha ao fechar janela de teste.");
      }

      process.Dispose();
    }
  }

  private async void AppRunnerOnBeforeMoveWindow(object? sender, EventArgs e)
  {
    try
    {
      await SuspendPreviewCaptureAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Falha ao suspender captura de preview antes de mover janela.");
    }
  }

  private void AppRunnerOnAfterMoveWindow(object? sender, EventArgs e)
  {
    SchedulePreviewResume();
  }

  private async Task SuspendPreviewCaptureAsync()
  {
    if (monitorPreviewDisplay is null)
    {
      return;
    }

    await monitorPreviewDisplay.SuspendCaptureAsync().ConfigureAwait(true);
  }

  private void SchedulePreviewResume()
  {
    if (IsDisposed || !IsHandleCreated)
    {
      return;
    }

    try
    {
      BeginInvoke(new Action(ResumePreviewCaptureWithDelay));
    }
    catch (ObjectDisposedException)
    {
      // Ignorar quando o formulário estiver sendo finalizado.
    }
    catch (InvalidOperationException)
    {
      // Ignorar quando o handle não estiver disponível.
    }
  }

  private async void ResumePreviewCaptureWithDelay()
  {
    try
    {
      try
      {
        await Task.Delay(PreviewResumeDelay).ConfigureAwait(true);
      }
      catch (TaskCanceledException)
      {
        return;
      }

      if (IsDisposed)
      {
        return;
      }

      await ResumePreviewCaptureAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Falha ao retomar captura de preview com atraso.");
    }
  }

  private async Task ResumePreviewCaptureAsync()
  {
    if (monitorPreviewDisplay is null)
    {
      return;
    }

    await monitorPreviewDisplay.ResumeCaptureAsync().ConfigureAwait(true);
  }

  private void AppEditorForm_Disposed(object? sender, EventArgs e)
  {
    StopCycleSimulation();
    DisposeSimRectDisplays();
    _appRunner.BeforeMoveWindow -= AppRunnerOnBeforeMoveWindow;
    _appRunner.AfterMoveWindow -= AppRunnerOnAfterMoveWindow;
    if (monitorPreviewDisplay is not null)
    {
      monitorPreviewDisplay.SimRectMoved -= MonitorPreviewDisplay_OnSimRectMoved;
    }
    CancelHoverThrottleTimer();
    _logScope.Dispose();
  }

  private async Task AppsTab_OpenRequestedAsync(object? sender, AppExecutionRequestEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(e.ExecutablePath) || !File.Exists(e.ExecutablePath))
    {
      WinForms.MessageBox.Show(
          this,
          "Selecione um executável válido para abrir.",
          "Abrir aplicativo",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Warning);
      return;
    }

    try
    {
      await Task.Run(() => LaunchExecutable(e.ExecutablePath, e.Arguments)).ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      WinForms.MessageBox.Show(
          this,
          $"Não foi possível abrir o aplicativo selecionado: {ex.Message}",
          "Abrir aplicativo",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Error);
    }
  }

  private async Task AppsTab_TestRequestedAsync(object? sender, AppExecutionRequestEventArgs e)
  {
    if (!OperatingSystem.IsWindows())
    {
      WinForms.MessageBox.Show(
          this,
          "O teste de posicionamento está disponível apenas no Windows.",
          "Teste de aplicativo",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Information);
      return;
    }

    if (!TryBuildCurrentApp(
            "Teste de aplicativo",
            out var app,
            out var monitor,
            out var bounds,
            e.ExecutablePath,
            e.Arguments,
            requireExecutable: false))
    {
      return;
    }

    var executablePath = app.ExecutablePath;
    var hasExecutable = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);

    try
    {
      if (hasExecutable)
      {
        UseWaitCursor = true;
        WinForms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;

        try
        {
          await _appRunner.RunAndPositionAsync(app, monitor, bounds).ConfigureAwait(true);
        }
        finally
        {
          WinForms.Cursor.Current = System.Windows.Forms.Cursors.Default;
          UseWaitCursor = false;
        }
      }
      else
      {
        await LaunchDummyWindowAsync(monitor, app.Window).ConfigureAwait(true);
      }
    }
    catch (Exception ex)
    {
      WinForms.MessageBox.Show(
          this,
          $"Não foi possível testar a posição: {ex.Message}",
          "Teste de aplicativo",
          WinForms.MessageBoxButtons.OK,
          WinForms.MessageBoxIcon.Error);
    }
  }
}
