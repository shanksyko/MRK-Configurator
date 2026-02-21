#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Mieruka.Core.Models;

namespace Mieruka.App.Services;

internal static class BrowserRegistry
{
    internal sealed record class BrowserInstallation(
        string Id,
        string DisplayName,
        BrowserType? Engine,
        string? ExecutablePath,
        bool IsDetected)
    {
        public bool IsSupported => Engine is not null;
    }

    private sealed record class BrowserDefinition(
        string Id,
        string DisplayName,
        BrowserType? Engine,
        string[] Executables,
        string[]? WindowsRelativeDirectories = null);

    private static readonly BrowserDefinition[] KnownBrowsers =
    {
        new(
            Id: "chrome",
            DisplayName: "Google Chrome",
            Engine: BrowserType.Chrome,
            Executables: new[] { "chrome.exe", "chrome" },
            WindowsRelativeDirectories: new[] { @"Google\Chrome\Application" }),
        new(
            Id: "edge",
            DisplayName: "Microsoft Edge",
            Engine: BrowserType.Edge,
            Executables: new[] { "msedge.exe", "microsoft-edge" },
            WindowsRelativeDirectories: new[] { @"Microsoft\Edge\Application" }),
        new(
            Id: "firefox",
            DisplayName: "Mozilla Firefox",
            Engine: BrowserType.Firefox,
            Executables: new[] { "firefox.exe", "firefox" },
            WindowsRelativeDirectories: new[] { @"Mozilla Firefox" }),
        new(
            Id: "brave",
            DisplayName: "Brave",
            Engine: BrowserType.Brave,
            Executables: new[] { "brave.exe", "brave-browser", "brave" },
            WindowsRelativeDirectories: new[] { @"BraveSoftware\Brave-Browser\Application" }),
        new(
            Id: "vivaldi",
            DisplayName: "Vivaldi",
            Engine: null,
            Executables: new[] { "vivaldi.exe", "vivaldi" },
            WindowsRelativeDirectories: new[] { @"Vivaldi\Application" }),
        new(
            Id: "opera",
            DisplayName: "Opera",
            Engine: null,
            Executables: new[] { "opera.exe", "launcher.exe", "opera" },
            WindowsRelativeDirectories: new[] { @"Opera" }),
    };

    public static IReadOnlyList<BrowserInstallation> Detect()
    {
        var results = new List<BrowserInstallation>(KnownBrowsers.Length);

        foreach (var browser in KnownBrowsers)
        {
            var path = FindExecutable(browser);
            results.Add(new BrowserInstallation(
                browser.Id,
                browser.DisplayName,
                browser.Engine,
                path,
                IsDetected: !string.IsNullOrWhiteSpace(path)));
        }

        return results;
    }

    private static string? FindExecutable(BrowserDefinition browser)
    {
        foreach (var executable in browser.Executables)
        {
            var candidate = FindWindowsExecutable(executable);
            if (IsValidCandidate(candidate))
            {
                return candidate;
            }

            candidate = TryCombineWithKnownDirectories(browser, executable);
            if (IsValidCandidate(candidate))
            {
                return candidate;
            }

            candidate = FindInPath(executable);
            if (IsValidCandidate(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindWindowsExecutable(string executable)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var subKey = $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executable}";

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

                    var direct = key.GetValue(string.Empty) as string;
                    if (IsValidCandidate(direct))
                    {
                        return direct;
                    }

                    var pathValue = key.GetValue("Path") as string;
                    if (!string.IsNullOrWhiteSpace(pathValue))
                    {
                        try
                        {
                            var combined = Path.Combine(pathValue, executable);
                            if (IsValidCandidate(combined))
                            {
                                return combined;
                            }
                        }
                        catch
                        {
                            // Ignorar falhas de combinação de caminho.
                        }
                    }
                }
                catch
                {
                    // Ignorar falhas de leitura do registro.
                }
            }
        }

        return null;
    }

    private static string? TryCombineWithKnownDirectories(BrowserDefinition browser, string executable)
    {
        if (!OperatingSystem.IsWindows() || browser.WindowsRelativeDirectories is null)
        {
            return null;
        }

        var roots = EnumerateWindowsRoots();

        foreach (var root in roots)
        {
            foreach (var relative in browser.WindowsRelativeDirectories)
            {
                try
                {
                    var candidate = Path.Combine(root, relative, executable);
                    if (IsValidCandidate(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignorar combinações inválidas.
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateWindowsRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFolder(roots, Environment.SpecialFolder.ProgramFiles);
        AddFolder(roots, Environment.SpecialFolder.ProgramFilesX86);
        AddFolder(roots, Environment.SpecialFolder.LocalApplicationData);
        AddFolder(roots, Environment.SpecialFolder.ApplicationData);

        return roots;

        static void AddFolder(ISet<string> accumulator, Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    accumulator.Add(path);
                }
            }
            catch
            {
                // Ignorar falhas ao acessar pastas especiais.
            }
        }
    }

    private static string? FindInPath(string executable)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var segment in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(segment, executable);
                if (IsValidCandidate(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignorar caminhos inválidos.
            }
        }

        return null;
    }

    private static bool IsValidCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
