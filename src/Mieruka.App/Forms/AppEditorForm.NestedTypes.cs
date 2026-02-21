#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct DEVMODE
  {
    private const int CchDeviceName = 32;
    private const int CchFormName = 32;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
    public string dmDeviceName;

    public short dmSpecVersion;
    public short dmDriverVersion;
    public short dmSize;
    public short dmDriverExtra;
    public int dmFields;
    public int dmPositionX;
    public int dmPositionY;
    public int dmDisplayOrientation;
    public int dmDisplayFixedOutput;
    public short dmColor;
    public short dmDuplex;
    public short dmYResolution;
    public short dmTTOption;
    public short dmCollate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
    public string dmFormName;

    public short dmLogPixels;
    public int dmBitsPerPel;
    public int dmPelsWidth;
    public int dmPelsHeight;
    public int dmDisplayFlags;
    public int dmDisplayFrequency;
    public int dmICMMethod;
    public int dmICMIntent;
    public int dmMediaType;
    public int dmDitherType;
    public int dmReserved1;
    public int dmReserved2;
    public int dmPanningWidth;
    public int dmPanningHeight;
  }

  private interface IInstalledAppsProvider
  {
    Task<IReadOnlyList<InstalledAppInfo>> QueryAsync();
  }

  private sealed class RegistryInstalledAppsProvider : IInstalledAppsProvider
  {
    public Task<IReadOnlyList<InstalledAppInfo>> QueryAsync()
    {
      return Task.Run<IReadOnlyList<InstalledAppInfo>>(InstalledAppsProvider.GetAll);
    }
  }

  private sealed class SimRectDisplay : IDisposable
  {
    public SimRectDisplay(Mieruka.App.Simulation.AppCycleSimulator.SimRect rect, WinForms.Panel panel, WinForms.Label label)
    {
      Rect = rect;
      Panel = panel;
      Label = label;
      NormalFont = label.Font;
      BoldFont = new Drawing.Font(label.Font, FontStyle.Bold);
      LastResult = SimRectStatus.None;
    }

    public Mieruka.App.Simulation.AppCycleSimulator.SimRect Rect { get; }

    public WinForms.Panel Panel { get; }

    public WinForms.Label Label { get; }

    public Drawing.Font NormalFont { get; }

    public Drawing.Font BoldFont { get; }

    public DateTime? LastActivation { get; set; }

    public DateTime? LastSkipped { get; set; }

    public SimRectStatus LastResult { get; set; }

    public void Dispose()
    {
      BoldFont.Dispose();
    }
  }

  private enum SimRectStatus
  {
    None,
    Completed,
    Skipped,
  }

  private sealed class MonitorOption
  {
    public MonitorOption(string? monitorId, MonitorInfo? monitor, string displayName)
    {
      MonitorId = monitorId;
      Monitor = monitor;
      DisplayName = displayName;
      Tag = monitor;
    }

    public string? MonitorId { get; }

    public MonitorInfo? Monitor { get; }

    public MonitorInfo? Tag { get; }

    public string DisplayName { get; }

    public static MonitorOption Empty()
        => new(null, null, "Nenhum monitor disponível");

    public override string ToString() => DisplayName;
  }

  private sealed class ProfileItemMetadata : INotifyPropertyChanged
  {
    private string _id;
    private int _order;
    private int _delayMs;
    private bool _askBeforeLaunch;
    private bool? _requiresNetwork;

    public ProfileItemMetadata(AppConfig? source, bool isTarget, int defaultOrder)
    {
      Original = source;
      IsTarget = isTarget;
      _id = source?.Id ?? string.Empty;
      var initialOrder = source?.Order ?? 0;
      _order = initialOrder > 0 ? initialOrder : Math.Max(1, defaultOrder);
      _delayMs = source?.DelayMs ?? 0;
      _askBeforeLaunch = source?.AskBeforeLaunch ?? false;
      _requiresNetwork = source?.RequiresNetwork;
    }

    public AppConfig? Original { get; }

    public bool IsTarget { get; }

    public string Id
    {
      get => _id;
      set
      {
        var normalized = value ?? string.Empty;
        if (_id != normalized)
        {
          _id = normalized;
          OnPropertyChanged(nameof(Id));
        }
      }
    }

    public int Order
    {
      get => _order;
      set
      {
        var normalized = value < 0 ? 0 : value;
        if (_order != normalized)
        {
          _order = normalized;
          OnPropertyChanged(nameof(Order));
        }
      }
    }

    public int DelayMs
    {
      get => _delayMs;
      set
      {
        var normalized = value < 0 ? 0 : value;
        if (_delayMs != normalized)
        {
          _delayMs = normalized;
          OnPropertyChanged(nameof(DelayMs));
        }
      }
    }

    public bool AskBeforeLaunch
    {
      get => _askBeforeLaunch;
      set
      {
        if (_askBeforeLaunch != value)
        {
          _askBeforeLaunch = value;
          OnPropertyChanged(nameof(AskBeforeLaunch));
        }
      }
    }

    public bool? RequiresNetwork
    {
      get => _requiresNetwork;
      set
      {
        if (_requiresNetwork != value)
        {
          _requiresNetwork = value;
          OnPropertyChanged(nameof(RequiresNetwork));
        }
      }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateFrom(AppConfig app)
    {
      Id = app.Id;
      Order = app.Order > 0 ? app.Order : Order;
      DelayMs = app.DelayMs;
      AskBeforeLaunch = app.AskBeforeLaunch;
      RequiresNetwork = app.RequiresNetwork;
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}
