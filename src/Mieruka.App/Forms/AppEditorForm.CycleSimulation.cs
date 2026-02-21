#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Mieruka.App.Simulation;
using Mieruka.Core.Config;
using Mieruka.Core.InstalledApps;
using Mieruka.Core.Models;
using Mieruka.App.Ui.PreviewBindings;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
  private void InitializeCycleSimulation()
  {
    if (flowCycleItems is null)
    {
      return;
    }

    _sites.ListChanged += Sites_ListChanged;

    if (txtId is not null)
    {
      txtId.TextChanged += (_, _) => RebuildSimRects();
    }

    if (cycleToolTip is not null)
    {
      if (btnCyclePlay is not null)
      {
        cycleToolTip.SetToolTip(btnCyclePlay, "Executa a simulação do ciclo.");
      }

      if (btnCycleStep is not null)
      {
        cycleToolTip.SetToolTip(btnCycleStep, "Avança manualmente para o próximo item do ciclo.");
      }

      if (btnCycleStop is not null)
      {
        cycleToolTip.SetToolTip(btnCycleStop, "Interrompe a simulação atual.");
      }

      if (chkCycleRedeDisponivel is not null)
      {
        cycleToolTip.SetToolTip(chkCycleRedeDisponivel, "Simula a disponibilidade de rede para itens dependentes.");
      }
    }

    RebuildSimRects();
  }

  private void ApplyAppTypeUI()
  {
    using var scope = _tabEditCoordinator.BeginEditScope(nameof(ApplyAppTypeUI));
    if (!scope.IsActive)
    {
      _logger.Debug("ApplyAppTypeUI: exit scope inactive");
      return;
    }

    var isExecutable = rbExe?.Checked ?? false;
    var isBrowser = rbBrowser?.Checked ?? false;
    _logger.Debug(
        "ApplyAppTypeUI: enter isExecutable={IsExecutable} isBrowser={IsBrowser}",
        isExecutable,
        isBrowser);

    var tabControl = tabEditor;
    var appsTabPage = tabAplicativos;
    var sitesTabPage = tabSites;

    if (tabControl is not null && appsTabPage is not null && sitesTabPage is not null)
    {
      SetTabVisibility(tabControl, appsTabPage, _tabAplicativosIndex, isExecutable);
      SetTabVisibility(tabControl, sitesTabPage, _tabSitesIndex, !isExecutable);

      if (isExecutable && ReferenceEquals(tabControl.SelectedTab, sitesTabPage))
      {
        tabControl.SelectedTab = tabControl.TabPages.Contains(appsTabPage)
            ? appsTabPage
            : (tabControl.TabPages.Count > 0 ? tabControl.TabPages[0] : null);
      }
      else if (!isExecutable && ReferenceEquals(tabControl.SelectedTab, appsTabPage))
      {
        tabControl.SelectedTab = tabControl.TabPages.Contains(sitesTabPage)
            ? sitesTabPage
            : (tabControl.TabPages.Count > 0 ? tabControl.TabPages[0] : null);
      }

      if (tabControl.SelectedTab is not null && !tabControl.TabPages.Contains(tabControl.SelectedTab))
      {
        tabControl.SelectedTab = tabControl.TabPages.Count > 0 ? tabControl.TabPages[0] : null;
      }
    }

    if (appsTabControl is not null)
    {
      appsTabControl.Enabled = isExecutable;
    }

    if (btnBrowseExe is not null)
    {
      btnBrowseExe.Visible = isExecutable;
      btnBrowseExe.Enabled = isExecutable;
      btnBrowseExe.TabStop = isExecutable;
    }

    if (txtExecutavel is not null)
    {
      txtExecutavel.Enabled = isExecutable;
      txtExecutavel.ReadOnly = !isExecutable;
      txtExecutavel.TabStop = isExecutable;
    }

    if (txtArgumentos is not null)
    {
      txtArgumentos.Enabled = isExecutable;
      txtArgumentos.ReadOnly = !isExecutable;
      txtArgumentos.TabStop = isExecutable;
    }

    if (isBrowser)
    {
      BindDetectedBrowsers();
    }
    else
    {
      if (cmbBrowserEngine is not null)
      {
        cmbBrowserEngine.Enabled = false;
      }

      if (pnlBrowserPanel is not null)
      {
        pnlBrowserPanel.Enabled = false;
      }

      if (sitesEditorControl is not null)
      {
        sitesEditorControl.Enabled = false;
      }
    }

    if (cmbBrowserEngine is not null)
    {
      cmbBrowserEngine.Visible = isBrowser;
      cmbBrowserEngine.TabStop = isBrowser && cmbBrowserEngine.Enabled;
    }

    if (lblBrowserDetected is not null)
    {
      lblBrowserDetected.Visible = isBrowser && !string.IsNullOrWhiteSpace(lblBrowserDetected.Text);
    }

    if (pnlBrowserPanel is not null)
    {
      pnlBrowserPanel.Visible = isBrowser;
    }

    if (sitesEditorControl is not null)
    {
      sitesEditorControl.Visible = isBrowser;
    }

    var selectedTabName = tabEditor?.SelectedTab?.Name ?? tabEditor?.SelectedTab?.Text ?? string.Empty;
    _logger.Information(
        "ApplyAppTypeUI: exit selectedTab={SelectedTab} isExecutable={IsExecutable} isBrowser={IsBrowser}",
        selectedTabName,
        isExecutable,
        isBrowser);
  }

  private static void SetTabVisibility(TabControl tabControl, TabPage tabPage, int originalIndex, bool visible)
  {
    var pages = tabControl.TabPages;
    var contains = pages.Contains(tabPage);

    if (visible)
    {
      if (contains)
      {
        return;
      }

      var insertIndex = originalIndex;
      if (insertIndex < 0 || insertIndex > pages.Count)
      {
        insertIndex = pages.Count;
      }

      pages.Insert(insertIndex, tabPage);
    }
    else if (contains)
    {
      pages.Remove(tabPage);
    }
  }

  private async Task EnsureAppsListAsync()
  {
    if (rbExe?.Checked != true)
    {
      return;
    }

    if (_appsListLoaded)
    {
      return;
    }

    if (IsDisposed || !IsHandleCreated)
    {
      return;
    }

    _appsListLoaded = true;

    UseWaitCursor = true;
    var previousCursor = Cursor.Current;
    Cursor.Current = Cursors.WaitCursor;

    try
    {
      var apps = await _installedAppsProvider.QueryAsync().ConfigureAwait(true);
      appsTabControl?.SetInstalledApps(apps);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Falha ao carregar a lista de aplicativos instalados.");
      _appsListLoaded = false;
    }
    finally
    {
      Cursor.Current = previousCursor;
      UseWaitCursor = false;
      ApplyAppTypeUI();
    }
  }

  private void BindInstalledBrowsers()
  {
    if (cmbNavegadores is null)
    {
      return;
    }

    var browsers = BrowserDiscovery.GetInstalledBrowsers().ToList();

    cmbNavegadores.DisplayMember = nameof(BrowserInfo.Name);
    cmbNavegadores.ValueMember = nameof(BrowserInfo.ExecutablePath);
    cmbNavegadores.DataSource = browsers;
    cmbNavegadores.Enabled = browsers.Count > 0;

    if (browsers.Count > 0)
    {
      cmbNavegadores.SelectedIndex = 0;
    }
    else
    {
      cmbNavegadores.SelectedIndex = -1;
    }
  }

  private void BindDetectedBrowsers()
  {
    if (cmbBrowserEngine is null || lblBrowserDetected is null || pnlBrowserPanel is null)
    {
      return;
    }

    var detections = BrowserRegistry.Detect();

    var supported = detections
        .Where(browser => browser.IsSupported)
        .ToList();

    cmbBrowserEngine.BeginUpdate();
    try
    {
      cmbBrowserEngine.Items.Clear();
      foreach (var browser in supported)
      {
        cmbBrowserEngine.Items.Add(new BrowserComboItem(browser));
      }
    }
    finally
    {
      cmbBrowserEngine.EndUpdate();
    }

    var items = new List<BrowserComboItem>(cmbBrowserEngine.Items.Count);
    foreach (var obj in cmbBrowserEngine.Items)
    {
      if (obj is BrowserComboItem item)
      {
        items.Add(item);
      }
    }

    BrowserComboItem? selected = items
        .FirstOrDefault(item => item.Installation.Engine == BrowserType.Chrome && item.Installation.IsDetected)
        ?? items.FirstOrDefault(item => item.Installation.IsDetected)
        ?? items.FirstOrDefault(item => item.Installation.Engine == BrowserType.Chrome)
        ?? items.FirstOrDefault();

    if (selected is not null)
    {
      cmbBrowserEngine.SelectedItem = selected;
    }
    else
    {
      cmbBrowserEngine.SelectedIndex = -1;
    }

    lblBrowserDetected.Text = BuildBrowserDetectionMessage(detections);

    var hasSupported = items.Count > 0;
    cmbBrowserEngine.Enabled = hasSupported;
    pnlBrowserPanel.Enabled = hasSupported;

    if (sitesEditorControl is not null)
    {
      sitesEditorControl.Enabled = hasSupported;
    }
  }

  private void cmbNavegadores_SelectedIndexChanged(object? sender, EventArgs e)
  {
    if (cmbNavegadores?.SelectedItem is not BrowserInfo browser)
    {
      return;
    }

    if (txtExecutavel is null)
    {
      return;
    }

    if (string.IsNullOrWhiteSpace(browser.ExecutablePath))
    {
      return;
    }

    txtExecutavel.Text = browser.ExecutablePath;
    txtExecutavel.SelectionStart = txtExecutavel.TextLength;
  }

  private static string BuildBrowserDetectionMessage(IReadOnlyList<BrowserRegistry.BrowserInstallation> detections)
  {
    if (detections.Count == 0)
    {
      return "Nenhum navegador foi detectado.";
    }

    List<string>? supportedDetected = null;
    List<string>? supportedMissing = null;
    List<string>? unsupportedDetected = null;

    foreach (var detection in detections)
    {
      if (detection.IsSupported)
      {
        if (detection.IsDetected)
        {
          (supportedDetected ??= new List<string>()).Add(detection.DisplayName);
        }
        else
        {
          (supportedMissing ??= new List<string>()).Add(detection.DisplayName);
        }
      }
      else if (detection.IsDetected)
      {
        (unsupportedDetected ??= new List<string>()).Add(detection.DisplayName);
      }
    }

    var builder = new StringBuilder();

    if (supportedDetected is { Count: > 0 })
    {
      builder.Append("Navegadores suportados detectados: ");
      builder.Append(string.Join(", ", supportedDetected));
      builder.Append('.');
    }
    else
    {
      builder.Append("Nenhum navegador suportado foi encontrado.");
    }

    if (supportedMissing is { Count: > 0 })
    {
      if (builder.Length > 0)
      {
        builder.Append(' ');
      }

      builder.Append("Não encontrados: ");
      builder.Append(string.Join(", ", supportedMissing));
      builder.Append('.');
    }

    if (unsupportedDetected is { Count: > 0 })
    {
      if (builder.Length > 0)
      {
        builder.Append(' ');
      }

      builder.Append("Detectados (não suportados): ");
      builder.Append(string.Join(", ", unsupportedDetected));
      builder.Append('.');
    }

    return builder.ToString();
  }

  private sealed class BrowserComboItem
  {
    internal BrowserComboItem(BrowserRegistry.BrowserInstallation installation)
    {
      Installation = installation;
    }

    internal BrowserRegistry.BrowserInstallation Installation { get; }

    internal BrowserType Engine => Installation.Engine ?? BrowserType.Chrome;

    public override string ToString()
    {
      return Installation.IsDetected
          ? Installation.DisplayName
          : $"{Installation.DisplayName} (não encontrado)";
    }
  }

  private void Sites_ListChanged(object? sender, ListChangedEventArgs e)
  {
    RebuildSimRects();
  }

  private void RebuildSimRects()
  {
    using var logicalDepth = new MonitorPreviewDisplay.PreviewLogicalScope(nameof(RebuildSimRects), _logger);
    if (!logicalDepth.Entered)
    {
      _logger.Error("RebuildSimRects: preview logical depth limit reached; aborting rebuild");
      return;
    }

    using var depth = new MonitorPreviewDisplay.PreviewCallScope(nameof(RebuildSimRects), _logger);
    if (!depth.Entered)
    {
      _logger.Error("RebuildSimRects: depth limit reached; aborting rebuild");
      return;
    }

    var currentDepth = Interlocked.Increment(ref _simRectsDepth);
    if (currentDepth > 8)
    {
      _logger.Error("sim_rects_depth_limit_reached depth={Depth} limit={Limit}", currentDepth, 8);
      Interlocked.Decrement(ref _simRectsDepth);
      return;
    }

    if (_inRebuildSimRects)
    {
      _logger.Debug("RebuildSimRects: recursion blocked");
      Interlocked.Decrement(ref _simRectsDepth);
      return;
    }

    _inRebuildSimRects = true;
    try
    {
      _logger.Debug(
          "RebuildSimRects: enter isDisposed={IsDisposed} simulationActive={SimulationActive}",
          IsDisposed,
          _cycleSimulationCts is not null);

      if (IsDisposed)
      {
        _logger.Debug("RebuildSimRects: exit form disposed");
        return;
      }

      if (_cycleSimulationCts is not null)
      {
        StopCycleSimulation();

        if (IsHandleCreated)
        {
          BeginInvoke(new Action(RebuildSimRects));
        }

        _logger.Information("RebuildSimRects: exit deferred due to active simulation");
        return;
      }

      _nextSimRectIndex = 0;
      ClearActiveSimRect();

      if (flowCycleItems is null)
      {
        _logger.Information("RebuildSimRects: exit missing cycle container");
        return;
      }

      flowCycleItems.SuspendLayout();
      try
      {
        flowCycleItems.Controls.Clear();
      }
      finally
      {
        flowCycleItems.ResumeLayout(false);
      }

      DisposeSimRectDisplays();

      var items = BuildSimRectList();
      if (items.Count == 0)
      {
        var placeholder = new WinForms.Label
        {
          AutoSize = true,
          Margin = new WinForms.Padding(12),
          Text = "Nenhum item disponível para simulação.",
        };

        flowCycleItems.Controls.Add(placeholder);
        UpdateCycleControlsState();
        _logger.Information("RebuildSimRects: exit no items generated");
        return;
      }

      flowCycleItems.SuspendLayout();
      try
      {
        foreach (var rect in items)
        {
          var display = CreateSimRectDisplay(rect);
          _cycleDisplays.Add(display);
          flowCycleItems.Controls.Add(display.Panel);
          ApplySimRectTooltip(display);
        }
      }
      finally
      {
        flowCycleItems.ResumeLayout(false);
      }

      UpdateCycleControlsState();
      _logger.Information(
          "RebuildSimRects: exit createdDisplays={DisplayCount} totalItems={ItemCount}",
          _cycleDisplays.Count,
          items.Count);
    }
    finally
    {
      _inRebuildSimRects = false;
      var depthAfter = Interlocked.Decrement(ref _simRectsDepth);
      if (depthAfter < 0)
      {
        Interlocked.Exchange(ref _simRectsDepth, 0);
      }
    }
  }

  private IReadOnlyList<AppCycleSimulator.SimRect> BuildSimRectList()
  {
    var items = new List<AppCycleSimulator.SimRect>();

    var appId = txtId?.Text?.Trim();
    var appLabel = string.IsNullOrWhiteSpace(appId) ? "Aplicativo" : appId;
    var appDetails = txtExecutavel is null
        ? null
        : (string.IsNullOrWhiteSpace(txtExecutavel.Text) ? null : txtExecutavel.Text.Trim());

    items.Add(new AppCycleSimulator.SimRect(
        "app",
        $"Aplicativo: {appLabel}",
        RequiresNetwork: false,
        DelayMs: 400,
        Details: appDetails));

    var index = 0;
    foreach (var site in _sites)
    {
      index++;
      var siteId = string.IsNullOrWhiteSpace(site.Id) ? $"Site {index}" : site.Id.Trim();
      var details = string.IsNullOrWhiteSpace(site.Url) ? null : site.Url.Trim();

      items.Add(new AppCycleSimulator.SimRect(
          $"site:{index}",
          $"Site: {siteId}",
          RequiresNetwork: true,
          DelayMs: null,
          Details: details));
    }

    return items;
  }

  private SimRectDisplay CreateSimRectDisplay(AppCycleSimulator.SimRect rect)
  {
    var panel = new WinForms.Panel
    {
      Width = 200,
      Height = 120,
      Margin = new WinForms.Padding(12),
      Padding = new WinForms.Padding(4),
      BorderStyle = BorderStyle.FixedSingle,
      BackColor = System.Drawing.SystemColors.ControlLightLight,
    };

    var label = new WinForms.Label
    {
      Dock = WinForms.DockStyle.Fill,
      AutoSize = false,
      TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
      Text = rect.DisplayName,
      AutoEllipsis = true,
      Padding = new WinForms.Padding(4),
    };

    panel.Controls.Add(label);
    panel.Tag = rect;

    return new SimRectDisplay(rect, panel, label);
  }

  private void ApplySimRectTooltip(SimRectDisplay display)
  {
    if (cycleToolTip is null)
    {
      return;
    }

    var builder = new StringBuilder();
    builder.Append(display.Rect.DisplayName);
    builder.AppendLine();
    builder.Append("Rede necessária: ");
    builder.Append(display.Rect.RequiresNetwork ? "Sim" : "Não");
    builder.AppendLine();
    builder.Append("Duração simulada: ");
    var delay = display.Rect.DelayMs ?? AppCycleSimulator.DefaultDelayMs;
    builder.Append(delay.ToString(CultureInfo.InvariantCulture));
    builder.Append(" ms");

    if (!string.IsNullOrWhiteSpace(display.Rect.Details))
    {
      builder.AppendLine();
      builder.Append(display.Rect.Details);
    }

    if (display.LastResult == SimRectStatus.Completed && display.LastActivation is DateTime completed)
    {
      builder.AppendLine();
      builder.Append("Última execução: ");
      builder.Append(completed.ToString("T", CultureInfo.CurrentCulture));
    }
    else if (display.LastResult == SimRectStatus.Skipped && display.LastSkipped is DateTime skipped)
    {
      builder.AppendLine();
      builder.Append("Ignorado às ");
      builder.Append(skipped.ToString("T", CultureInfo.CurrentCulture));
      builder.Append(" (rede indisponível)");
    }

    var tooltip = builder.ToString();
    cycleToolTip.SetToolTip(display.Panel, tooltip);
    cycleToolTip.SetToolTip(display.Label, tooltip);
  }

  private void RefreshSimRectTooltips()
  {
    foreach (var display in _cycleDisplays)
    {
      ApplySimRectTooltip(display);
    }
  }

  private async Task RunContinuousSimulationAsync()
  {
    if (_cycleSimulationCts is not null || _cycleDisplays.Count == 0)
    {
      return;
    }

    var tokenSource = new CancellationTokenSource();
    _cycleSimulationCts = tokenSource;
    UpdateCycleControlsState();

    try
    {
      var token = tokenSource.Token;
      var singleRect = new AppCycleSimulator.SimRect[1];

      while (!token.IsCancellationRequested)
      {
        if (_cycleDisplays.Count == 0)
        {
          break;
        }

        var display = _cycleDisplays[_nextSimRectIndex];
        _nextSimRectIndex = (_nextSimRectIndex + 1) % _cycleDisplays.Count;

        if (display.Rect.RequiresNetwork && !EvaluateNetworkAvailability())
        {
          display.LastResult = SimRectStatus.Skipped;
          display.LastActivation = null;
          display.LastSkipped = DateTime.Now;
          ApplySimRectTooltip(display);

          if (_cycleDisplays.All(d => d.Rect.RequiresNetwork))
          {
            await Task.Delay(AppCycleSimulator.DefaultDelayMs, token);
          }

          continue;
        }

        display.LastResult = SimRectStatus.None;
        display.LastActivation = null;
        display.LastSkipped = null;
        ApplySimRectTooltip(display);

        singleRect[0] = display.Rect;
        await _cycleSimulator.SimulateAsync(
            singleRect,
            EvaluateNetworkAvailability,
            OnSimRectStarted,
            OnSimRectEnded,
            token);
      }
    }
    catch (OperationCanceledException)
    {
      // Ignorar cancelamentos solicitados pelo usuário.
    }
    finally
    {
      _cycleSimulationCts = null;
      tokenSource.Dispose();
      UpdateCycleControlsState();
      ClearActiveSimRect();
    }
  }

  private async Task RunSingleStepAsync()
  {
    if (_cycleSimulationCts is not null || _cycleDisplays.Count == 0)
    {
      return;
    }

    if (btnCycleStep is not null)
    {
      btnCycleStep.Enabled = false;
    }

    try
    {
      var singleRect = new AppCycleSimulator.SimRect[1];
      var attempts = 0;
      while (attempts < _cycleDisplays.Count)
      {
        var display = _cycleDisplays[_nextSimRectIndex];
        _nextSimRectIndex = (_nextSimRectIndex + 1) % _cycleDisplays.Count;
        attempts++;

        if (display.Rect.RequiresNetwork && !EvaluateNetworkAvailability())
        {
          display.LastResult = SimRectStatus.Skipped;
          display.LastActivation = null;
          display.LastSkipped = DateTime.Now;
          ApplySimRectTooltip(display);
          continue;
        }

        display.LastResult = SimRectStatus.None;
        display.LastActivation = null;
        display.LastSkipped = null;
        ApplySimRectTooltip(display);

        singleRect[0] = display.Rect;
        await _cycleSimulator.SimulateAsync(
            singleRect,
            EvaluateNetworkAvailability,
            OnSimRectStarted,
            OnSimRectEnded);

        break;
      }
    }
    finally
    {
      UpdateCycleControlsState();
    }
  }

  private void StopCycleSimulation()
  {
    var cts = _cycleSimulationCts;
    if (cts is null)
    {
      return;
    }

    if (!cts.IsCancellationRequested)
    {
      cts.Cancel();
    }

    UpdateCycleControlsState();
  }

  private void UpdateCycleControlsState()
  {
    var hasItems = _cycleDisplays.Count > 0;
    var isRunning = _cycleSimulationCts is not null;

    if (btnCyclePlay is not null)
    {
      btnCyclePlay.Enabled = hasItems && !isRunning;
    }

    if (btnCycleStep is not null)
    {
      btnCycleStep.Enabled = hasItems && !isRunning;
    }

    if (btnCycleStop is not null)
    {
      btnCycleStop.Enabled = isRunning;
    }
  }

  private void OnSimRectStarted(AppCycleSimulator.SimRect rect)
  {
    var display = FindDisplay(rect);
    if (display is null)
    {
      return;
    }

    if (_activeCycleDisplay is not null && !ReferenceEquals(_activeCycleDisplay, display))
    {
      SetSimRectActive(_activeCycleDisplay, isActive: false);
    }

    SetSimRectActive(display, isActive: true);
    _activeCycleDisplay = display;
  }

  private void OnSimRectEnded(AppCycleSimulator.SimRect rect)
  {
    var display = FindDisplay(rect);
    if (display is null)
    {
      return;
    }

    display.LastResult = SimRectStatus.Completed;
    display.LastActivation = DateTime.Now;
    display.LastSkipped = null;
    ApplySimRectTooltip(display);

    if (_activeCycleDisplay is not null && ReferenceEquals(_activeCycleDisplay, display))
    {
      SetSimRectActive(display, isActive: false);
      _activeCycleDisplay = null;
    }
  }

  private SimRectDisplay? FindDisplay(AppCycleSimulator.SimRect rect)
  {
    return _cycleDisplays.FirstOrDefault(display => ReferenceEquals(display.Rect, rect));
  }

  private void SetSimRectActive(SimRectDisplay display, bool isActive)
  {
    if (display.Panel.IsDisposed)
    {
      return;
    }

    if (isActive)
    {
      display.Panel.BorderStyle = BorderStyle.Fixed3D;
      display.Panel.BackColor = Drawing.Color.FromArgb(32, 146, 204);
      display.Label.ForeColor = Drawing.Color.White;
      display.Label.Font = display.BoldFont;
    }
    else
    {
      display.Panel.BorderStyle = BorderStyle.FixedSingle;
      display.Panel.BackColor = System.Drawing.SystemColors.ControlLightLight;
      display.Label.ForeColor = System.Drawing.SystemColors.ControlText;
      display.Label.Font = display.NormalFont;
    }
  }

  private void ClearActiveSimRect()
  {
    if (_activeCycleDisplay is null)
    {
      return;
    }

    SetSimRectActive(_activeCycleDisplay, isActive: false);
    _activeCycleDisplay = null;
  }

  private bool EvaluateNetworkAvailability()
  {
    return chkCycleRedeDisponivel?.Checked ?? true;
  }

  private void DisposeSimRectDisplays()
  {
    ClearActiveSimRect();

    foreach (var display in _cycleDisplays)
    {
      try
      {
        if (!display.Panel.IsDisposed)
        {
          display.Panel.Dispose();
        }
      }
      catch (Exception ex)
      {
        _logger.Debug(ex, "Falha ao descartar painel do ciclo.");
      }

      display.Dispose();
    }

    _cycleDisplays.Clear();
  }

  private async void btnCyclePlay_Click(object? sender, EventArgs e)
  {
    try
    {
      await RunContinuousSimulationAsync();
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Falha não tratada em btnCyclePlay_Click.");
    }
  }

  private async void btnCycleStep_Click(object? sender, EventArgs e)
  {
    try
    {
      await RunSingleStepAsync();
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Falha não tratada em btnCycleStep_Click.");
    }
  }

  private void btnCycleStop_Click(object? sender, EventArgs e)
  {
    StopCycleSimulation();
  }
}
