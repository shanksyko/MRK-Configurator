using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Services;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services;

/// <summary>
/// Provides registration and management of global keyboard shortcuts using the Win32 API.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly object _gate = new();
    private readonly ITelemetry _telemetry;
    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HotkeySink? _sink;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyManager"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to log registration failures and trigger events.</param>
    public HotkeyManager(ITelemetry? telemetry = null)
    {
        _telemetry = telemetry ?? NullTelemetry.Instance;

        if (OperatingSystem.IsWindows())
        {
            _sink = new HotkeySink();
        }
    }

    /// <summary>
    /// Registers or updates the gesture associated with a specific hotkey.
    /// </summary>
    /// <param name="key">Unique key used to identify the hotkey registration.</param>
    /// <param name="displayName">Human friendly name used in telemetry logs.</param>
    /// <param name="gesture">Gesture to be registered (e.g. <c>Ctrl+Alt+P</c>).</param>
    /// <param name="handler">Callback invoked when the gesture is triggered.</param>
    public void RegisterOrUpdate(string key, string displayName, string? gesture, Action handler)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Registration key cannot be empty.", nameof(key));
        }

        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(handler);
        EnsureNotDisposed();

        if (_sink is null)
        {
            return;
        }

        var normalized = NormalizeGesture(gesture);

        lock (_gate)
        {
            if (_registrations.TryGetValue(key, out var existing))
            {
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    _sink.Unregister(existing.Id);
                    _registrations.Remove(key);
                    return;
                }

                if (string.Equals(existing.Gesture, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _registrations[key] = existing with { Handler = handler, DisplayName = displayName };
                    return;
                }

                _sink.Unregister(existing.Id);
                _registrations.Remove(key);
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!HotkeyBinding.TryParse(normalized, out var binding))
            {
                _telemetry.Warn($"Invalid hotkey '{gesture}' for '{displayName}'.");
                return;
            }

            try
            {
                var id = _sink.Register(binding, () => OnHotkeyInvoked(key));
                _registrations[key] = new Registration(id, normalized, displayName, handler);
            }
            catch (Exception ex)
            {
                _telemetry.Error($"Failed to register hotkey '{normalized}' for '{displayName}'.", ex);
            }
        }
    }

    /// <summary>
    /// Removes the registration associated with the specified key, if any.
    /// </summary>
    /// <param name="key">Unique key identifying the registration.</param>
    public void Unregister(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (_sink is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_registrations.TryGetValue(key, out var registration))
            {
                _sink.Unregister(registration.Id);
                _registrations.Remove(key);
            }
        }
    }

    /// <summary>
    /// Releases resources used by the manager and unregisters all gestures.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_sink is not null)
        {
            lock (_gate)
            {
                foreach (var registration in _registrations.Values)
                {
                    _sink.Unregister(registration.Id);
                }

                _registrations.Clear();
            }

            _sink.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnHotkeyInvoked(string key)
    {
        Registration? registration;

        lock (_gate)
        {
            if (!_registrations.TryGetValue(key, out var existing))
            {
                return;
            }

            registration = existing;
        }

        if (registration is null)
        {
            return;
        }

        _telemetry.Info($"Hotkey '{registration.DisplayName}' triggered using '{registration.Gesture}'.");

        try
        {
            registration.Handler();
        }
        catch (Exception ex)
        {
            _telemetry.Error($"Unhandled exception while executing hotkey '{registration.DisplayName}'.", ex);
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HotkeyManager));
        }
    }

    private static string? NormalizeGesture(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return null;
        }

        var parts = gesture
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Select(part => part.ToUpperInvariant());

        var normalized = string.Join('+', parts);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record class Registration(int Id, string Gesture, string DisplayName, Action Handler);

    private readonly record struct HotkeyBinding(uint Modifiers, uint Key)
    {
        public uint Modifiers { get; } = Modifiers;
        public uint Key { get; } = Key;

        public static bool TryParse(string? gesture, out HotkeyBinding binding)
        {
            binding = default;

            if (string.IsNullOrWhiteSpace(gesture))
            {
                return false;
            }

            var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            uint modifiers = 0;
            uint key = 0;

            foreach (var token in tokens)
            {
                var upper = token.Trim().ToUpperInvariant();

                switch (upper)
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= NativeMethods.MOD_CONTROL;
                        continue;

                    case "ALT":
                        modifiers |= NativeMethods.MOD_ALT;
                        continue;

                    case "SHIFT":
                        modifiers |= NativeMethods.MOD_SHIFT;
                        continue;

                    case "WIN":
                    case "WINDOWS":
                    case "SUPER":
                    case "META":
                        modifiers |= NativeMethods.MOD_WIN;
                        continue;
                }

                if (TryParseKey(upper, out var parsedKey))
                {
                    key = parsedKey;
                    continue;
                }

                return false;
            }

            if (key == 0)
            {
                return false;
            }

            binding = new HotkeyBinding(modifiers, key);
            return true;
        }

        private static bool TryParseKey(string token, out uint key)
        {
            if (Enum.TryParse(token, true, out WinForms.Keys parsed) && parsed != WinForms.Keys.None)
            {
                key = (uint)(parsed & WinForms.Keys.KeyCode);
                return key != 0;
            }

            if (token.Length == 1)
            {
                key = (uint)char.ToUpperInvariant(token[0]);
                return true;
            }

            if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(token.AsSpan(1), out var functionKey) &&
                functionKey is >= 1 and <= 24)
            {
                key = (uint)(WinForms.Keys.F1 + functionKey - 1);
                return true;
            }

            key = 0;
            return false;
        }
    }

    private sealed class HotkeySink : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _windowReady = new(false);
        private readonly ManualResetEventSlim _threadExited = new(false);
        private HotkeyWindow? _window;
        private SynchronizationContext? _context;
        private bool _disposed;

        public HotkeySink()
        {
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "GlobalHotkeys",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _windowReady.Wait();
        }

        public int Register(HotkeyBinding binding, Action handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HotkeySink));
            }

            var tcs = new TaskCompletionSource<int>();
            _context!.Post(_ =>
            {
                try
                {
                    tcs.SetResult(_window!.Register(binding, handler));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task.GetAwaiter().GetResult();
        }

        public void Unregister(int registrationId)
        {
            if (_disposed || _context is null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            _context.Post(_ =>
            {
                _window?.Unregister(registrationId);
                tcs.SetResult(true);
            }, null);

            tcs.Task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_context is null)
            {
                _windowReady.Dispose();
                _threadExited.Dispose();
                return;
            }

            _context.Post(_ =>
            {
                _window?.Dispose();
                WinForms.Application.ExitThread();
            }, null);

            _threadExited.Wait();
            _windowReady.Dispose();
            _threadExited.Dispose();
        }

        private void ThreadMain()
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            _context = SynchronizationContext.Current;

            using var window = new HotkeyWindow(action => Task.Run(action));
            window.CreateHandle(new CreateParams());
            _window = window;
            _windowReady.Set();

            WinForms.Application.Run();

            _window = null;
            _threadExited.Set();
        }
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Dictionary<int, Action> _handlers = new();
        private readonly Action<Action> _dispatch;
        private int _nextId;

        public HotkeyWindow(Action<Action> dispatch)
        {
            _dispatch = dispatch;
        }

        public int Register(HotkeyBinding binding, Action handler)
        {
            if (Handle == IntPtr.Zero)
            {
                CreateHandle(new CreateParams());
            }

            var id = ++_nextId;
            if (!NativeMethods.RegisterHotKey(Handle, id, binding.Modifiers, binding.Key))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register hotkey.");
            }

            _handlers[id] = handler;
            return id;
        }

        public void Unregister(int id)
        {
            if (_handlers.Remove(id))
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                var id = (int)m.WParam;
                if (_handlers.TryGetValue(id, out var handler))
                {
                    _dispatch(handler);
                }

                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var id in _handlers.Keys.ToList())
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }

            _handlers.Clear();
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }

    private static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

}
