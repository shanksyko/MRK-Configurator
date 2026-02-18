using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mieruka.Core.Services;

/// <summary>
/// Monitors interactive sessions and exposes state information about remote desktop connections.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionChecker : IDisposable
{
    private readonly Lock _gate = new();
    private readonly ITelemetry _telemetry;
    private readonly Dictionary<int, SessionSnapshot> _sessions = new();
    private bool _disposed;
    private bool _initialized;
    private bool _hasDisconnectedRemoteSession;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionChecker"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to log session activity.</param>
    public SessionChecker(ITelemetry? telemetry = null)
    {
        _telemetry = telemetry ?? NullTelemetry.Instance;
    }

    /// <summary>
    /// Gets a value indicating whether a remote desktop session is in a disconnected state.
    /// </summary>
    public bool HasDisconnectedRemoteSession
    {
        get
        {
            lock (_gate)
            {
                return _hasDisconnectedRemoteSession;
            }
        }
    }

    /// <summary>
    /// Updates the internal snapshot of sessions and returns whether a remote session is disconnected.
    /// </summary>
    /// <returns><c>true</c> when any remote desktop session is disconnected; otherwise, <c>false</c>.</returns>
    public bool UpdateStatus()
    {
        EnsureNotDisposed();

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        lock (_gate)
        {
            if (!TryEnumerateSessions(out var sessions))
            {
                return _hasDisconnectedRemoteSession;
            }

            if (!_initialized)
            {
                _sessions.Clear();

                foreach (var session in sessions)
                {
                    _sessions[session.SessionId] = session;
                }

                _hasDisconnectedRemoteSession = ContainsDisconnectedRemoteSession(sessions);
                _initialized = true;
                return _hasDisconnectedRemoteSession;
            }

            var seen = new HashSet<int>();

            foreach (var session in sessions)
            {
                seen.Add(session.SessionId);

                if (_sessions.TryGetValue(session.SessionId, out var previous))
                {
                    HandleTransition(previous, session);
                    _sessions[session.SessionId] = session;
                }
                else
                {
                    _sessions[session.SessionId] = session;
                    LogInitialState(session);
                }
            }

            if (_sessions.Count > seen.Count)
            {
                var removals = new List<int>();

                foreach (var pair in _sessions)
                {
                    if (!seen.Contains(pair.Key))
                    {
                        LogRemoval(pair.Value);
                        removals.Add(pair.Key);
                    }
                }

                foreach (var sessionId in removals)
                {
                    _sessions.Remove(sessionId);
                }
            }

            _hasDisconnectedRemoteSession = ContainsDisconnectedRemoteSession(sessions);
            return _hasDisconnectedRemoteSession;
        }
    }

    /// <summary>
    /// Releases resources used by the instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _sessions.Clear();
            _initialized = false;
            _hasDisconnectedRemoteSession = false;
            _disposed = true;
        }
    }

    private static bool ContainsDisconnectedRemoteSession(IReadOnlyList<SessionSnapshot> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.IsRemote && session.State == WTSConnectStateClass.WTSDisconnected)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleTransition(SessionSnapshot previous, SessionSnapshot current)
    {
        if (!previous.IsActive && current.IsActive)
        {
            LogLogon(current);
            return;
        }

        if (previous.IsActive && !current.IsActive)
        {
            LogLogoff(current);
        }
    }

    private void LogInitialState(SessionSnapshot session)
    {
        if (session.State == WTSConnectStateClass.WTSDisconnected && session.IsRemote)
        {
            _telemetry.Info($"session remote-disconnected {FormatIdentity(session)}");
        }
    }

    private void LogRemoval(SessionSnapshot session)
    {
        if (session.IsActive)
        {
            LogLogoff(session);
        }
        else if (session.State == WTSConnectStateClass.WTSDisconnected && session.IsRemote)
        {
            _telemetry.Info($"session remote-removed {FormatIdentity(session)}");
        }
    }

    private void LogLogon(SessionSnapshot session)
    {
        var kind = session.IsRemote ? "remote" : "console";
        _telemetry.Info($"session logon {kind} {FormatIdentity(session)}");
    }

    private void LogLogoff(SessionSnapshot session)
    {
        var kind = session.IsRemote ? "remote" : "console";
        var state = session.State == WTSConnectStateClass.WTSDisconnected ? "disconnect" : "logoff";
        _telemetry.Info($"session {state} {kind} {FormatIdentity(session)}");
    }

    private static string FormatIdentity(SessionSnapshot session)
    {
        var user = string.IsNullOrWhiteSpace(session.UserName) ? "unknown" : session.UserName;
        var domain = string.IsNullOrWhiteSpace(session.Domain) ? "" : $"{session.Domain}\\";
        return $"{domain}{user} (ID {session.SessionId})";
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SessionChecker));
        }
    }

    private static bool TryEnumerateSessions(out List<SessionSnapshot> sessions)
    {
        sessions = new List<SessionSnapshot>();

        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count) || buffer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (count <= 0)
            {
                return true;
            }

            var dataSize = Marshal.SizeOf<WTS_SESSION_INFO>();
            var current = buffer;

            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(current);
                current = IntPtr.Add(current, dataSize);

                var userName = QueryString(info.SessionId, WTSInfoClass.WTSUserName);
                var domain = QueryString(info.SessionId, WTSInfoClass.WTSDomainName);
                var protocolType = QueryProtocol(info.SessionId);

                sessions.Add(new SessionSnapshot(
                    info.SessionId,
                    userName,
                    domain,
                    info.State,
                    protocolType == WtsClientProtocolTypes.Rdp));
            }

            return true;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static string? QueryString(int sessionId, WTSInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytesReturned) ||
            buffer == IntPtr.Zero || bytesReturned <= 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static WtsClientProtocolTypes QueryProtocol(int sessionId)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTSInfoClass.WTSClientProtocolType, out var buffer, out var bytesReturned) ||
            buffer == IntPtr.Zero || bytesReturned < sizeof(short))
        {
            return WtsClientProtocolTypes.Console;
        }

        try
        {
            var value = (ushort)Marshal.ReadInt16(buffer);

            return value switch
            {
                2 => WtsClientProtocolTypes.Rdp,
                1 => WtsClientProtocolTypes.Ica,
                _ => WtsClientProtocolTypes.Console,
            };
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WTSInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    private enum WtsClientProtocolTypes
    {
        Console = 0,
        Ica = 1,
        Rdp = 2,
    }

    private readonly struct SessionSnapshot
    {
        public SessionSnapshot(int sessionId, string? userName, string? domain, WTSConnectStateClass state, bool isRemote)
        {
            SessionId = sessionId;
            UserName = userName;
            Domain = domain;
            State = state;
            IsRemote = isRemote;
        }

        public int SessionId { get; }

        public string? UserName { get; }

        public string? Domain { get; }

        public WTSConnectStateClass State { get; }

        public bool IsRemote { get; }

        public bool IsActive => State == WTSConnectStateClass.WTSActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr WinStationName;
        public WTSConnectStateClass State;
    }

    private enum WTSInfoClass
    {
        WTSInitialProgram = 0,
        WTSApplicationName = 1,
        WTSWorkingDirectory = 2,
        WTSOEMId = 3,
        WTSSessionId = 4,
        WTSUserName = 5,
        WTSWinStationName = 6,
        WTSDomainName = 7,
        WTSConnectState = 8,
        WTSClientBuildNumber = 9,
        WTSClientName = 10,
        WTSClientDirectory = 11,
        WTSClientProductId = 12,
        WTSClientHardwareId = 13,
        WTSClientAddress = 14,
        WTSClientDisplay = 15,
        WTSClientProtocolType = 16,
    }
}

/// <summary>
/// Represents the connection state of a Terminal Services session.
/// </summary>
public enum WTSConnectStateClass
{
    WTSActive,
    WTSConnected,
    WTSConnectQuery,
    WTSShadow,
    WTSDisconnected,
    WTSIdle,
    WTSListen,
    WTSReset,
    WTSDown,
    WTSInit,
}
