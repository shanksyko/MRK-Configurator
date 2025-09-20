using System;
using System.Collections.Generic;
using Mieruka.Core.Models;

namespace Mieruka.Core.Services;

/// <summary>
/// Provides access to the display topology available on the machine.
/// </summary>
public interface IDisplayService : IDisposable
{
    /// <summary>
    /// Occurs when the monitor topology changes.
    /// </summary>
    event EventHandler? TopologyChanged;

    /// <summary>
    /// Retrieves the monitors that are currently active.
    /// </summary>
    /// <returns>A read-only list that represents the active monitors.</returns>
    IReadOnlyList<MonitorInfo> Monitors();

    /// <summary>
    /// Finds a monitor by its unique key.
    /// </summary>
    /// <param name="key">Monitor identifier.</param>
    /// <returns>Monitor information when the monitor exists; otherwise, <see langword="null"/>.</returns>
    MonitorInfo? FindBy(MonitorKey key);

    /// <summary>
    /// Finds a monitor by the device name exposed by the operating system.
    /// </summary>
    /// <param name="deviceName">Monitor device name.</param>
    /// <returns>Monitor information when the monitor exists; otherwise, <see langword="null"/>.</returns>
    MonitorInfo? FindByDeviceName(string deviceName);
}
