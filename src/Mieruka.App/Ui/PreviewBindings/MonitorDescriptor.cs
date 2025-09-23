using System;
using Mieruka.Core.Models;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Concrete implementation of <see cref="IMonitorDescriptor"/> used across the UI layer.
/// </summary>
public sealed class MonitorDescriptor : IMonitorDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorDescriptor"/> class.
    /// </summary>
    public MonitorDescriptor(MonitorInfo monitor, string id, string displayName)
    {
        Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? string.Empty;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public MonitorInfo Monitor { get; }

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}
