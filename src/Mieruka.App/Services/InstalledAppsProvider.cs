using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Mieruka.App.Services;

public static class InstalledAppsProvider
{
    private const int MaxExecutableCandidates = 200;
    private const int MaxShortcutCandidates = 40;

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

                var executable = ResolveExecutablePath(displayName, iconPath, installLocation, uninstall);
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

    private static string? ResolveExecutablePath(string displayName, string? displayIcon, string? installLocation, string? uninstallCommand)
    {
        var candidate = ExtractExecutable(displayIcon);
        if (LooksLikeLauncher(candidate) && !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        var installCandidate = FindLauncherExecutable(displayName, installLocation);
        if (!string.IsNullOrWhiteSpace(installCandidate))
        {
            return installCandidate;
        }

        candidate = ExtractExecutable(uninstallCommand);
        if (LooksLikeLauncher(candidate) && !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        var appPathCandidate = ResolveFromAppPaths(displayName);
        if (!string.IsNullOrWhiteSpace(appPathCandidate))
        {
            return appPathCandidate;
        }

        var startMenuCandidate = ResolveFromStartMenuShortcuts(displayName);
        if (!string.IsNullOrWhiteSpace(startMenuCandidate))
        {
            return startMenuCandidate;
        }

        return null;
    }

    private static readonly string[] _badTokens =
    {
        "uninstall", "setup", "install", "updater", "update", "repair"
    };

    private static bool LooksLikeLauncher(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim().Trim('\"');

        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(trimmed).ToLowerInvariant();

        foreach (var token in _badTokens)
        {
            if (name.Contains(token))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindLauncherExecutable(string displayName, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        try
        {
            var candidates = Directory
                .EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories)
                .Take(MaxExecutableCandidates)
                .Where(path => LooksLikeLauncher(path) && File.Exists(path))
                .Select(path => new { Path = path, Score = ScoreCandidate(displayName, path) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Path.Length)
                .ToList();

            return candidates.FirstOrDefault()?.Path;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveFromAppPaths(string displayName)
    {
        var best = ((string? Path, int Score)) (null, -1);
        var roots = new[]
        {
            Registry.CurrentUser,
            Registry.LocalMachine,
        };

        foreach (var root in roots)
        {
            using var appPaths = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
            if (appPaths is null)
            {
                continue;
            }

            foreach (var subKeyName in appPaths.GetSubKeyNames())
            {
                using var subKey = appPaths.OpenSubKey(subKeyName);
                if (subKey is null)
                {
                    continue;
                }

                var rawPath = Convert.ToString(subKey.GetValue(null));
                var candidatePath = ExtractExecutable(rawPath);
                if (!LooksLikeLauncher(candidatePath) || string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                {
                    continue;
                }

                var score = ScoreCandidate(displayName, candidatePath);

                var friendlyName = Convert.ToString(subKey.GetValue("FriendlyAppName"));
                if (!string.IsNullOrWhiteSpace(friendlyName) && NamesSimilar(displayName, friendlyName))
                {
                    score = Math.Max(score, 50);
                }
                else if (NamesSimilar(displayName, Path.GetFileNameWithoutExtension(subKeyName)))
                {
                    score = Math.Max(score, 40);
                }

                if (score <= 0)
                {
                    continue;
                }

                if (score > best.Score)
                {
                    best = (candidatePath, score);
                }
            }
        }

        return best.Path;
    }

    private static string? ResolveFromStartMenuShortcuts(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var startMenuRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };

        var candidates = new List<(string Path, int Score)>();

        foreach (var root in startMenuRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory
                    .EnumerateFiles(
                        root,
                        "*.lnk",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.System
                                | FileAttributes.Temporary
                                | FileAttributes.Offline
                                | FileAttributes.ReparsePoint,
                        })
                    .Take(MaxShortcutCandidates);
            }
            catch
            {
                continue;
            }

            try
            {
                foreach (var shortcut in shortcuts)
                {
                    var score = ScoreCandidate(displayName, shortcut);
                    if (score <= 0)
                    {
                        continue;
                    }

                    candidates.Add((shortcut, score));
                }
            }
            catch
            {
                continue;
            }
        }

        foreach (var candidate in candidates
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var target = ResolveShortcutTarget(candidate.Path);
            if (LooksLikeLauncher(target) && !string.IsNullOrWhiteSpace(target) && File.Exists(target))
            {
                return target;
            }
        }

        return null;
    }

    private static string? ResolveShortcutTarget(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
        {
            return null;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                try
                {
                    var target = shortcut.TargetPath as string;
                    return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
                }
                finally
                {
                    TryReleaseComObject(shortcut);
                }
            }
            finally
            {
                TryReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void TryReleaseComObject(object? value)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static int ScoreCandidate(string displayName, string path)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return 0;
        }

        var displayNormalized = NormalizeName(displayName);
        if (displayNormalized.Length == 0)
        {
            return 0;
        }

        var candidateName = Path.GetFileNameWithoutExtension(path);
        var candidateNormalized = NormalizeName(candidateName);
        if (candidateNormalized.Length == 0)
        {
            return 0;
        }

        if (string.Equals(candidateNormalized, displayNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (candidateNormalized.Contains(displayNormalized, StringComparison.Ordinal))
        {
            return 80;
        }

        if (displayNormalized.Contains(candidateNormalized, StringComparison.Ordinal))
        {
            return 70;
        }

        var displayTokens = Tokenize(displayName).ToList();
        if (displayTokens.Count == 0)
        {
            return 0;
        }

        var candidateTokens = Tokenize(candidateName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = displayTokens.Count(token => candidateTokens.Contains(token));

        return matches * 10;
    }

    private static bool NamesSimilar(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftNormalized = NormalizeName(left);
        var rightNormalized = NormalizeName(right);

        if (leftNormalized.Length == 0 || rightNormalized.Length == 0)
        {
            return false;
        }

        if (string.Equals(leftNormalized, rightNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return leftNormalized.Contains(rightNormalized, StringComparison.Ordinal) ||
               rightNormalized.Contains(leftNormalized, StringComparison.Ordinal);
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
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
