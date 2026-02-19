#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Services;

namespace Mieruka.Automation.Native;

/// <summary>
/// Executes JSON-described input action sequences against native application windows.
/// Supports keyboard (WM_KEYDOWN/WM_KEYUP/WM_CHAR via PostMessage) and mouse (mouse_event).
/// </summary>
public sealed class NativeAppAutomator
{
    private readonly ITelemetry _telemetry;
    private IntPtr _targetWindow = IntPtr.Zero;

    public NativeAppAutomator(ITelemetry? telemetry = null)
    {
        _telemetry = telemetry ?? NullTelemetry.Instance;
    }

    /// <summary>
    /// Executes all steps in <paramref name="sequence"/> sequentially.
    /// </summary>
    public async Task ExecuteAsync(ActionSequenceConfig sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        _telemetry.Info($"Executing action sequence '{sequence.Name ?? "(unnamed)"}' with {sequence.Actions.Count} steps.");

        foreach (var step in sequence.Actions)
        {
            await ExecuteStepAsync(step).ConfigureAwait(false);
        }
    }

    private async Task ExecuteStepAsync(ActionStep step)
    {
        switch (step.Type.ToLowerInvariant())
        {
            case "focus_window":
                ExecuteFocusWindow(step.Title);
                break;

            case "key":
                ExecuteKey(step.Key, keyDown: true);
                ExecuteKey(step.Key, keyDown: false);
                break;

            case "key_down":
                ExecuteKey(step.Key, keyDown: true);
                break;

            case "key_up":
                ExecuteKey(step.Key, keyDown: false);
                break;

            case "type":
                ExecuteType(step.Text);
                break;

            case "mouse_move":
                ExecuteMouseMove(step.X ?? 0, step.Y ?? 0);
                break;

            case "mouse_click":
                ExecuteMouseClick(step.X ?? 0, step.Y ?? 0, step.Button ?? "left");
                break;

            case "mouse_down":
                ExecuteMouseButtonState(step.X, step.Y, step.Button ?? "left", down: true);
                break;

            case "mouse_up":
                ExecuteMouseButtonState(step.X, step.Y, step.Button ?? "left", down: false);
                break;

            case "wait":
                var ms = step.Ms ?? 100;
                if (ms > 0)
                {
                    await Task.Delay(ms).ConfigureAwait(false);
                }
                break;
        }
    }

    // ── Step implementations ──────────────────────────────────────────────────

    private void ExecuteFocusWindow(string? titleFragment)
    {
        if (string.IsNullOrWhiteSpace(titleFragment))
        {
            return;
        }

        var handle = FindWindowByTitleFragment(titleFragment);
        if (handle == IntPtr.Zero)
        {
            _telemetry.Warn($"Window with title fragment '{titleFragment}' not found.");
            return;
        }

        _targetWindow = handle;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            User32.SetForegroundWindow(handle);
        }
        catch (Exception ex)
        {
            _telemetry.Info("Failed to set foreground window.", ex);
        }
    }

    private void ExecuteKey(string? keyName, bool keyDown)
    {
        if (string.IsNullOrWhiteSpace(keyName) || !OperatingSystem.IsWindows())
        {
            return;
        }

        var vk = ResolveVirtualKey(keyName);
        if (vk == 0)
        {
            _telemetry.Warn($"Unknown virtual key: '{keyName}'.");
            return;
        }

        const uint WmKeyDown = 0x0100;
        const uint WmKeyUp = 0x0101;
        var msg = keyDown ? WmKeyDown : WmKeyUp;

        var hwnd = _targetWindow != IntPtr.Zero ? _targetWindow : User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            User32.PostMessage(hwnd, msg, new IntPtr(vk), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _telemetry.Info($"Failed to send key '{keyName}'.", ex);
        }
    }

    private void ExecuteType(string? text)
    {
        if (string.IsNullOrEmpty(text) || !OperatingSystem.IsWindows())
        {
            return;
        }

        const uint WmChar = 0x0102;
        var hwnd = _targetWindow != IntPtr.Zero ? _targetWindow : User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        foreach (var ch in text)
        {
            try
            {
                User32.PostMessage(hwnd, WmChar, new IntPtr((int)ch), IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _telemetry.Info($"Failed to type character '{ch}'.", ex);
            }
        }
    }

    private static void ExecuteMouseMove(int x, int y)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var absX = (uint)ToAbsoluteCoord(x, isX: true);
        var absY = (uint)ToAbsoluteCoord(y, isX: false);
        User32.mouse_event(User32.MOUSEEVENTF_MOVE | User32.MOUSEEVENTF_ABSOLUTE, absX, absY, 0, UIntPtr.Zero);
    }

    private static void ExecuteMouseClick(int x, int y, string button)
    {
        ExecuteMouseButtonState(x, y, button, down: true);
        ExecuteMouseButtonState(x, y, button, down: false);
    }

    private static void ExecuteMouseButtonState(int? x, int? y, string button, bool down)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (downFlag, upFlag) = button.ToLowerInvariant() switch
        {
            "right" => (User32.MOUSEEVENTF_RIGHTDOWN, User32.MOUSEEVENTF_RIGHTUP),
            "middle" => (User32.MOUSEEVENTF_MIDDLEDOWN, User32.MOUSEEVENTF_MIDDLEUP),
            _ => (User32.MOUSEEVENTF_LEFTDOWN, User32.MOUSEEVENTF_LEFTUP),
        };

        var flags = down ? downFlag : upFlag;
        uint absX = 0;
        uint absY = 0;

        if (x.HasValue && y.HasValue)
        {
            flags |= User32.MOUSEEVENTF_MOVE | User32.MOUSEEVENTF_ABSOLUTE;
            absX = (uint)ToAbsoluteCoord(x.Value, isX: true);
            absY = (uint)ToAbsoluteCoord(y.Value, isX: false);
        }

        User32.mouse_event(flags, absX, absY, 0, UIntPtr.Zero);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static IntPtr FindWindowByTitleFragment(string titleFragment)
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        IntPtr found = IntPtr.Zero;

        User32.EnumWindows((hwnd, _) =>
        {
            if (!User32.IsWindowVisible(hwnd))
            {
                return true;
            }

            var sb = new StringBuilder(256);
            User32.GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.IndexOf(titleFragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = hwnd;
                return false; // stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static int ToAbsoluteCoord(int pixel, bool isX)
    {
        var screenSize = isX
            ? User32.GetSystemMetrics(User32.SM_CXSCREEN)
            : User32.GetSystemMetrics(User32.SM_CYSCREEN);

        if (screenSize <= 0)
        {
            return pixel;
        }

        return (int)((pixel * 65535.0) / screenSize);
    }

    private static int ResolveVirtualKey(string keyName) => keyName.ToUpperInvariant() switch
    {
        "TAB" => 0x09,
        "ENTER" or "RETURN" => 0x0D,
        "ESCAPE" or "ESC" => 0x1B,
        "SPACE" => 0x20,
        "LEFT" => 0x25,
        "UP" => 0x26,
        "RIGHT" => 0x27,
        "DOWN" => 0x28,
        "DELETE" or "DEL" => 0x2E,
        "BACKSPACE" => 0x08,
        "HOME" => 0x24,
        "END" => 0x23,
        "PAGEUP" => 0x21,
        "PAGEDOWN" => 0x22,
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        "CTRL" or "CONTROL" => 0x11,
        "ALT" => 0x12,
        "SHIFT" => 0x10,
        "WIN" or "WINDOWS" => 0x5B,
        _ when keyName.Length == 1 => (int)keyName[0],
        _ => 0,
    };
}
