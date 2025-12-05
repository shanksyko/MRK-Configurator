#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Mieruka.Core.InstalledApps;

public sealed record class BrowserInfo(string Name, string ExecutablePath);

public static class BrowserDiscovery
{
    private sealed record class BrowserCandidate(string Name, string StartMenuKey, string[] Executables);

    private static readonly BrowserCandidate[] Candidates =
    {
        new("Microsoft Edge", "Microsoft Edge", new[] { "msedge.exe" }),
        new("Google Chrome", "Google Chrome", new[] { "chrome.exe" }),
        new("Mozilla Firefox", "Firefox", new[] { "firefox.exe" }),
        new("Opera", "OperaStable", new[] { "opera.exe", "launcher.exe" }),
    };

    public static IReadOnlyList<BrowserInfo> GetInstalledBrowsers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<BrowserInfo>();
        }

        var browsers = new List<BrowserInfo>();

        foreach (var candidate in Candidates)
        {
            var executablePath = FindExecutable(candidate);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                continue;
            }

            browsers.Add(new BrowserInfo(candidate.Name, executablePath));
        }

        return browsers;
    }

    private static string? FindExecutable(BrowserCandidate candidate)
    {
        foreach (var executable in candidate.Executables)
        {
            var fromAppPaths = ReadAppPathExecutable(executable);
            if (IsValidExecutable(fromAppPaths))
            {
                return fromAppPaths;
            }

            var fromStartMenu = ReadStartMenuExecutable(candidate.StartMenuKey, executable);
            if (IsValidExecutable(fromStartMenu))
            {
                return fromStartMenu;
            }
        }

        return null;
    }

    private static string? ReadAppPathExecutable(string executable)
    {
        const string keyTemplate = @"Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{0}";
        var subKey = string.Format(CultureInfo.InvariantCulture, keyTemplate, executable);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey);
                    if (key is null)
                    {
                        continue;
                    }

                    var defaultValue = NormalizePath(key.GetValue(string.Empty) as string);
                    if (IsValidExecutable(defaultValue))
                    {
                        return defaultValue;
                    }

                    var pathValue = key.GetValue("Path") as string;
                    if (string.IsNullOrWhiteSpace(pathValue))
                    {
                        continue;
                    }

                    try
                    {
                        var combined = NormalizePath(Path.Combine(pathValue, executable));
                        if (IsValidExecutable(combined))
                        {
                            return combined;
                        }
                    }
                    catch
                    {
                        // Ignore invalid combinations.
                    }
                }
                catch
                {
                    // Ignore registry read errors and fallback to the next candidate.
                }
            }
        }

        return null;
    }

    private static string? ReadStartMenuExecutable(string startMenuKey, string executable)
    {
        var subKey = @$"Software\\Clients\\StartMenuInternet\\{startMenuKey}\\shell\\open\\command";

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey);
                    var command = key?.GetValue(string.Empty) as string;
                    var parsed = ExtractExecutableFromCommand(command, executable);
                    if (IsValidExecutable(parsed))
                    {
                        return parsed;
                    }
                }
                catch
                {
                    // Ignore registry read errors and fallback to the next candidate.
                }
            }
        }

        return null;
    }

    private static string? ExtractExecutableFromCommand(string? command, string executable)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"') && trimmed.Contains('"'))
        {
            var endIndex = trimmed.IndexOf('"', 1);
            if (endIndex > 1)
            {
                return NormalizePath(trimmed.Substring(1, endIndex - 1));
            }
        }

        var firstSegment = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return null;
        }

        if (!string.Equals(Path.GetFileName(firstSegment), Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizePath(firstSegment);
    }

    private static string? NormalizePath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var path = candidate.Trim(' ', '\"');
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static bool IsValidExecutable(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            return File.Exists(candidate);
        }
        catch
        {
            return false;
        }
    }
}
