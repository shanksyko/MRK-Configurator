#if WINDOWS10_0_17763_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;

namespace Mieruka.Preview.Diagnostics;

/// <summary>
/// Provides diagnostics for Windows Graphics Capture availability on the current machine.
/// </summary>
public static class WgcDiagnosticTool
{
    private const string GraphicsCaptureSessionType = "Windows.Graphics.Capture.GraphicsCaptureSession";
    private static readonly Version MinimumOsVersion = new(10, 0, 17763, 0);

    /// <summary>
    /// Retrieves a diagnostic report describing whether Windows Graphics Capture can run on this host.
    /// </summary>
    public static WgcCompatibilityReport GetReport()
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var currentOsVersion = Environment.OSVersion.Version;

        if (!OperatingSystem.IsWindows())
        {
            errors.Add("Windows Graphics Capture está disponível apenas no Windows.");
            return BuildReport(currentOsVersion, warnings, errors, isElevated: false, isRdpSession: false, hasGraphicsCaptureType: false);
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(MinimumOsVersion.Major, MinimumOsVersion.Minor, MinimumOsVersion.Build))
        {
            errors.Add($"Windows Graphics Capture requer Windows 10 build {MinimumOsVersion.Build} ou superior. Versão atual: {currentOsVersion}.");
        }

        var isElevated = IsProcessElevated();
        if (isElevated)
        {
            errors.Add("Processo em modo elevado detectado. Execute o Mieruka Configurator como usuário padrão para habilitar Windows Graphics Capture.");
        }

        var isRdpSession = IsRdpSession();
        if (isRdpSession)
        {
            errors.Add("Sessão Remote Desktop detectada. Windows Graphics Capture não é suportado em RDP.");
        }

        var hasGraphicsCaptureType = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(MinimumOsVersion.Major, MinimumOsVersion.Minor, MinimumOsVersion.Build))
        {
            try
            {
                hasGraphicsCaptureType = ApiInformation.IsTypePresent(GraphicsCaptureSessionType);
            }
            catch (Exception ex)
            {
                warnings.Add($"Falha ao consultar GraphicsCaptureSession: {ex.Message}");
            }

            if (!hasGraphicsCaptureType)
            {
                errors.Add("Tipo WinRT Windows.Graphics.Capture.GraphicsCaptureSession não encontrado. Instale o Media Feature Pack (edições N) ou atualize o Windows.");
            }
        }

        if (hasGraphicsCaptureType)
        {
            try
            {
                if (!GraphicsCaptureSession.IsSupported())
                {
                    errors.Add("GraphicsCaptureSession.IsSupported retornou false para este dispositivo.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"GraphicsCaptureSession.IsSupported falhou: {ex.Message}");
            }
        }

        return BuildReport(currentOsVersion, warnings, errors, isElevated, isRdpSession, hasGraphicsCaptureType);
    }

    /// <summary>
    /// Creates a human friendly summary of a diagnostic failure.
    /// </summary>
    public static string BuildFailureMessage(WgcCompatibilityReport report)
    {
        var builder = new StringBuilder("Windows Graphics Capture não pôde ser inicializado.");
        if (report.Errors.Length > 0)
        {
            builder.Append(' ').Append(string.Join(" ", report.Errors));
        }

        if (report.Warnings.Length > 0)
        {
            builder.Append(' ').Append("Avisos: ").Append(string.Join(" ", report.Warnings));
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Determines whether the failure indicated by the report should be treated as permanent for this session.
    /// </summary>
    public static bool ShouldTreatAsPermanent(WgcCompatibilityReport report)
    {
        if (report.IsRdpSession && !report.IsElevated && report.HasGraphicsCaptureType)
        {
            return false;
        }

        if (report.CurrentOsVersion.CompareTo(report.MinRequiredOsVersion) < 0)
        {
            return true;
        }

        return report.IsElevated || !report.HasGraphicsCaptureType || report.Errors.Length > 0;
    }

    private static WgcCompatibilityReport BuildReport(
        Version currentOsVersion,
        List<string> warnings,
        List<string> errors,
        bool isElevated,
        bool isRdpSession,
        bool hasGraphicsCaptureType)
    {
        var normalizedWarnings = warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToArray();
        var normalizedErrors = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
        var isSupported = normalizedErrors.Length == 0;
        return new WgcCompatibilityReport(
            isSupported,
            MinimumOsVersion,
            currentOsVersion,
            isElevated,
            isRdpSession,
            hasGraphicsCaptureType,
            normalizedWarnings,
            normalizedErrors);
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity is null)
            {
                return false;
            }

            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRdpSession()
    {
        try
        {
            return SystemInformation.TerminalServerSession;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Immutable diagnostic report describing Windows Graphics Capture compatibility.
/// </summary>
/// <param name="IsSupported">Indicates whether WGC can be used right now.</param>
/// <param name="MinRequiredOsVersion">Minimum OS version required for WGC.</param>
/// <param name="CurrentOsVersion">Current OS version.</param>
/// <param name="IsElevated">Indicates whether the process is elevated.</param>
/// <param name="IsRdpSession">Indicates whether the session is running over RDP.</param>
/// <param name="HasGraphicsCaptureType">Indicates whether the WinRT GraphicsCaptureSession type is available.</param>
/// <param name="Warnings">Diagnostic warnings.</param>
/// <param name="Errors">Diagnostic errors.</param>
public record WgcCompatibilityReport(
    bool IsSupported,
    Version MinRequiredOsVersion,
    Version CurrentOsVersion,
    bool IsElevated,
    bool IsRdpSession,
    bool HasGraphicsCaptureType,
    string[] Warnings,
    string[] Errors);

#else
using System;

namespace Mieruka.Preview.Diagnostics;

public static class WgcDiagnosticTool
{
    private static readonly Version MinimumOsVersion = new(10, 0, 17763, 0);

    public static WgcCompatibilityReport GetReport()
        => new(
            false,
            MinimumOsVersion,
            Environment.OSVersion.Version,
            false,
            false,
            false,
            Array.Empty<string>(),
            new[] { "Windows Graphics Capture APIs não estão disponíveis neste runtime." });

    public static string BuildFailureMessage(WgcCompatibilityReport report)
        => "Windows Graphics Capture não está disponível neste ambiente.";

    public static bool ShouldTreatAsPermanent(WgcCompatibilityReport report) => true;
}

public record WgcCompatibilityReport(
    bool IsSupported,
    Version MinRequiredOsVersion,
    Version CurrentOsVersion,
    bool IsElevated,
    bool IsRdpSession,
    bool HasGraphicsCaptureType,
    string[] Warnings,
    string[] Errors);
#endif
