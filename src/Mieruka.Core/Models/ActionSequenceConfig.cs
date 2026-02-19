#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mieruka.Core.Models;

/// <summary>
/// Defines a sequence of input actions to be replayed against a native application window.
/// Actions are executed sequentially by <see cref="Mieruka.Automation.Native.NativeAppAutomator"/>.
/// </summary>
public sealed class ActionSequenceConfig
{
    /// <summary>Human-readable display name for this sequence.</summary>
    public string? Name { get; init; }

    /// <summary>Ordered list of steps to execute.</summary>
    public List<ActionStep> Actions { get; init; } = new();
}

/// <summary>A single step within an <see cref="ActionSequenceConfig"/>.</summary>
public sealed class ActionStep
{
    /// <summary>
    /// The action type.
    /// Supported values: <c>focus_window</c>, <c>key</c>, <c>key_down</c>, <c>key_up</c>,
    /// <c>type</c>, <c>mouse_move</c>, <c>mouse_click</c>, <c>mouse_down</c>, <c>mouse_up</c>, <c>wait</c>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>For <c>focus_window</c>: partial window title to match (case-insensitive).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>For <c>key</c>, <c>key_down</c>, <c>key_up</c>: virtual key name (e.g. "Tab", "F5", "Enter").</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>For <c>type</c>: text string to send as individual WM_CHAR messages.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>For mouse actions: X coordinate (screen pixels).</summary>
    [JsonPropertyName("x")]
    public int? X { get; init; }

    /// <summary>For mouse actions: Y coordinate (screen pixels).</summary>
    [JsonPropertyName("y")]
    public int? Y { get; init; }

    /// <summary>
    /// For <c>mouse_click</c>/<c>mouse_down</c>/<c>mouse_up</c>: mouse button.
    /// Supported: <c>left</c> (default), <c>right</c>, <c>middle</c>.
    /// </summary>
    [JsonPropertyName("button")]
    public string? Button { get; init; }

    /// <summary>For <c>wait</c>: delay in milliseconds.</summary>
    [JsonPropertyName("ms")]
    public int? Ms { get; init; }
}
