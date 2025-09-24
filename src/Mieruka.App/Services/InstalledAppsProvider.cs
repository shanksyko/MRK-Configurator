using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Mieruka.App.Services;

public static class InstalledAppsProvider
{
    private static readonly (RegistryKey Root, string Path, string Source)[] RegistryLocations =
    {
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM"),
        (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM32"),
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU"),
    };

    public static List<InstalledAppInfo> GetAll()
    {
        var apps = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in EnumerateRegistryEntries())
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.ExecutablePath))
            {
                continue;
            }

            var key = $"{entry.Name}|{entry.ExecutablePath}";
            if (!apps.ContainsKey(key))
            {
                apps[key] = entry;
            }
        }

        return apps.Values
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<InstalledAppInfo> EnumerateRegistryEntries()
    {
        foreach (var (root, path, source) in RegistryLocations)
        {
            using var baseKey = root.OpenSubKey(path);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var displayName = Convert.ToString(subKey.GetValue("DisplayName"));
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var iconPath = Convert.ToString(subKey.GetValue("DisplayIcon"));
                var installLocation = Convert.ToString(subKey.GetValue("InstallLocation"));
                var uninstall = Convert.ToString(subKey.GetValue("UninstallString"));

                var executable = ResolveExecutablePath(iconPath, installLocation, uninstall);
                if (string.IsNullOrWhiteSpace(executable))
                {
                    continue;
                }

                yield return new InstalledAppInfo(
                    Name: displayName.Trim(),
                    Version: Convert.ToString(subKey.GetValue("DisplayVersion"))?.Trim(),
                    Vendor: Convert.ToString(subKey.GetValue("Publisher"))?.Trim(),
                    ExecutablePath: executable,
                    Source: source);
            }
        }
    }

    private static string? ResolveExecutablePath(string? displayIcon, string? installLocation, string? uninstallCommand)
    {
        var candidate = ExtractExecutable(displayIcon);
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        candidate = ExtractExecutable(uninstallCommand);
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            try
            {
                var directoryFiles = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                return directoryFiles.FirstOrDefault();
            }
            catch
            {
                // Ignore IO failures.
            }
        }

        return null;
    }

    private static string? ExtractExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        trimmed = Environment.ExpandEnvironmentVariables(trimmed);

        if (trimmed.StartsWith('"') && trimmed.Count(c => c == '"') >= 2)
        {
            var secondQuote = trimmed.IndexOf('"', 1);
            if (secondQuote > 1)
            {
                trimmed = trimmed.Substring(1, secondQuote - 1);
            }
        }
        else
        {
            var separators = new[] { ',', ' ' };
            foreach (var separator in separators)
            {
                var index = trimmed.IndexOf(separator);
                if (index > 0)
                {
                    trimmed = trimmed.Substring(0, index);
                    break;
                }
            }
        }

        trimmed = trimmed.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed;
    }
}

public sealed record class InstalledAppInfo(
    string Name,
    string? Version,
    string? Vendor,
    string ExecutablePath,
    string Source);
